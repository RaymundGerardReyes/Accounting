using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Application.Kotobee;

public sealed class FulfillKotobeeOrderService
{
    private readonly IPaidOrderVerifier _paidOrderVerifier;
    private readonly IKotobeeApiClient _kotobee;
    private readonly IKotobeeEntitlementRepository _repository;
    private readonly IKotobeeCatalogMappingResolver _mappingResolver;
    private readonly IOptionsMonitor<KotobeeOptions> _optionsMonitor;
    private readonly ILogger<FulfillKotobeeOrderService> _logger;
    private readonly KotobeeSheetWriteBackCoordinator? _sheetCoordinator;

    public FulfillKotobeeOrderService(
        IPaidOrderVerifier paidOrderVerifier,
        IKotobeeApiClient kotobee,
        IKotobeeEntitlementRepository repository,
        IKotobeeCatalogMappingResolver mappingResolver,
        IOptionsMonitor<KotobeeOptions> optionsMonitor,
        ILogger<FulfillKotobeeOrderService> logger,
        KotobeeSheetWriteBackCoordinator? sheetCoordinator = null)
    {
        _paidOrderVerifier = paidOrderVerifier ?? throw new ArgumentNullException(nameof(paidOrderVerifier));
        _kotobee = kotobee ?? throw new ArgumentNullException(nameof(kotobee));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mappingResolver = mappingResolver ?? throw new ArgumentNullException(nameof(mappingResolver));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sheetCoordinator = sheetCoordinator;
    }

    public async Task FulfillPaidOrderAsync(KotobeeFulfillmentJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ct.ThrowIfCancellationRequested();

        // =========================================================================
        // STRICT PAID-ONLY GUARD (Layer 2):
        // Never call Kotobee unless the Payments module confirms Order == Paid.
        // =========================================================================
        var isPaid = await _paidOrderVerifier.IsOrderConfirmedPaidAsync(job.OrderId, ct).ConfigureAwait(false);
        if (!isPaid)
        {
            _logger.LogWarning(
                "Blocked Kotobee activation for Order {OrderId}: order is NOT in confirmed Paid status.",
                job.OrderId);
            return;
        }

        var opts = _optionsMonitor.CurrentValue;

        // Resolve purchased SKUs -> Kotobee book targets (supports single books & bundles)
        var targets = _mappingResolver.ResolveBooksForOrder(job.PurchasedProductSkus);
        if (targets.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Order {OrderId} contains no Kotobee-mapped products. Skipping.",
                    job.OrderId);
            }

            if (_sheetCoordinator is not null)
            {
                try
                {
                    var audit = new AuditExecutionContext(
                        ActorId: "kotobee:worker",
                        AuditReason: "No ebook items fulfillment status sync",
                        CorrelationId: job.OrderId);

                    await _sheetCoordinator.SyncPaidFulfillmentOutcomeAsync(
                        job.OrderId,
                        job.BuyerEmail,
                        productHint: string.Join(", ", job.PurchasedProductSkus),
                        auditContext: audit,
                        ct: ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to sync no-ebook outcome to Google Sheets for Order {OrderId}.",
                        job.OrderId);
                }
            }

            return;
        }

        // Load already-active entitlements so retries skip completed books (Idempotency Layer 3)
        var existingRecords = await _repository.GetByOrderIdAsync(job.OrderId, ct).ConfigureAwait(false);
        var alreadyActiveBookIds = existingRecords
            .Where(r => string.Equals(r.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.KotobeeBookId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // STEP 1: Retrieve the Kotobee user once by email
        var initialLookup = await _kotobee.GetUserAsync(
            email: job.BuyerEmail,
            uid: null,
            libraryId: opts.DefaultLibraryId,
            bookId: null,
            ct: ct).ConfigureAwait(false);

        var kotobeeUid = initialLookup.Uid;

        // Process each purchased book sequentially (respecting 1 req/sec limit)
        foreach (var target in targets)
        {
            if (alreadyActiveBookIds.Contains(target.BookId))
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Order {OrderId}, Book {BookId} already Active locally. Skipping duplicate call.",
                        job.OrderId, target.BookId);
                }
                continue;
            }

            var libId = target.LibraryId ?? opts.DefaultLibraryId;
            DateOnly? expiryDate = target.AccessDurationDays.HasValue
                ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(target.AccessDurationDays.Value))
                : null;

            await _repository.UpsertPendingAsync(new KotobeeEntitlementRecord(
                Id: Guid.NewGuid(),
                OrderId: job.OrderId,
                ProductSku: target.SourceSku,
                BuyerEmail: job.BuyerEmail,
                KotobeeUid: kotobeeUid,
                KotobeeMode: opts.PublishingMode,
                KotobeeLibId: libId,
                KotobeeBookId: target.BookId,
                Status: "Pending",
                BookAccessState: null,
                AttemptCount: 0,
                LastError: null,
                ExpiresAtUtc: expiryDate.HasValue
                    ? new DateTimeOffset(expiryDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
                    : null,
                ActivatedAtUtc: null), ct).ConfigureAwait(false);

            try
            {
                // STEP 2: Check individual book permission (if user already exists)
                if (!string.IsNullOrWhiteSpace(kotobeeUid))
                {
                    var permCheck = await _kotobee.GetUserAsync(
                        email: null,
                        uid: kotobeeUid,
                        libraryId: libId,
                        bookId: target.BookId,
                        ct: ct).ConfigureAwait(false);

                    if (string.Equals(permCheck.BookAccess, "full", StringComparison.OrdinalIgnoreCase))
                    {
                        await _repository.MarkActiveAsync(job.OrderId, target.BookId, kotobeeUid, "full", ct).ConfigureAwait(false);
                        continue;
                    }

                    // If user currently has "partial" chapter access, upgrade to full via /user/edit
                    if (string.Equals(permCheck.BookAccess, "partial", StringComparison.OrdinalIgnoreCase))
                    {
                        var editRes = await _kotobee.EditUserBookAccessAsync(
                            uid: kotobeeUid,
                            libraryId: libId,
                            bookId: target.BookId,
                            active: true,
                            accessExpirationDate: expiryDate,
                            ct: ct).ConfigureAwait(false);

                        if (!editRes.Success)
                        {
                            throw new InvalidOperationException($"Kotobee /user/edit failed: {editRes.ErrorMessage}");
                        }
                    }
                    else
                    {
                        // STEP 3A: Existing user without this book -> grant via /user/add with uid
                        var grantRes = await _kotobee.AddUserOrGrantBookAsync(
                            email: null,
                            uid: kotobeeUid,
                            fullName: job.BuyerName,
                            libraryId: libId,
                            bookId: target.BookId,
                            accessExpirationDate: expiryDate,
                            ct: ct).ConfigureAwait(false);

                        if (!grantRes.Success)
                        {
                            throw new InvalidOperationException($"Kotobee /user/add (existing user) failed: {grantRes.ErrorMessage}");
                        }
                    }
                }
                else
                {
                    // STEP 3B: Brand-new user -> create user + grant first book via /user/add with email
                    var createRes = await _kotobee.AddUserOrGrantBookAsync(
                        email: job.BuyerEmail,
                        uid: null,
                        fullName: job.BuyerName,
                        libraryId: libId,
                        bookId: target.BookId,
                        accessExpirationDate: expiryDate,
                        ct: ct).ConfigureAwait(false);

                    if (!createRes.Success)
                    {
                        throw new InvalidOperationException($"Kotobee /user/add (new user) failed: {createRes.ErrorMessage}");
                    }

                    kotobeeUid = createRes.Uid;
                }

                // STEP 4: Verify activation via /user/get before marking locally Active
                var verify = await _kotobee.GetUserAsync(
                    email: string.IsNullOrWhiteSpace(kotobeeUid) ? job.BuyerEmail : null,
                    uid: kotobeeUid,
                    libraryId: libId,
                    bookId: target.BookId,
                    ct: ct).ConfigureAwait(false);

                kotobeeUid ??= verify.Uid;

                if (!string.Equals(verify.BookAccess, "full", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Verification failed for Book {target.BookId}: expected bookAccess='full', got '{verify.BookAccess}'.");
                }

                await _repository.MarkActiveAsync(job.OrderId, target.BookId, kotobeeUid, verify.BookAccess, ct).ConfigureAwait(false);

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Activated Kotobee Book {BookId} for Order {OrderId} (UID: {Uid}).",
                        target.BookId, job.OrderId, kotobeeUid);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed Kotobee activation for Order {OrderId}, Book {BookId}.",
                    job.OrderId, target.BookId);
                await _repository.MarkFailedAsync(job.OrderId, target.BookId, ex.Message, ct).ConfigureAwait(false);
            }
        }

        // =========================================================================
        // CLOSED-LOOP DYNAMIC GOOGLE SHEET STATUS WRITE-BACK:
        // Summarizes outcomes (ACTIVATED / PARTIAL_FAILED / FAILED) and writes to Sheet.
        // Isolated: failure never rolls back Kotobee or crashes the background worker.
        // =========================================================================
        if (_sheetCoordinator is not null)
        {
            try
            {
                var audit = new AuditExecutionContext(
                    ActorId: "kotobee:worker",
                    AuditReason: "Fulfillment complete status sync",
                    CorrelationId: job.OrderId);

                await _sheetCoordinator.SyncPaidFulfillmentOutcomeAsync(
                    job.OrderId,
                    job.BuyerEmail,
                    productHint: string.Join(", ", job.PurchasedProductSkus),
                    auditContext: audit,
                    ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to sync fulfillment outcome to Google Sheets for Order {OrderId}. Background retry available.",
                    job.OrderId);
            }
        }
    }
}
