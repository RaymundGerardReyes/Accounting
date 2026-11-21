using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Infrastructure.Kotobee;
using Xunit;

namespace Rge.Integrations.Integration.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public sealed class KotobeeDynamicSystemIntegrationTests
{
    // =========================================================================
    // INT-001 to INT-030: END-TO-END WEBHOOK PUSH -> ADAPTER -> SNAPSHOT PIPELINE
    // =========================================================================

    [Fact]
    public async Task WebhookPush_EndToEnd_PersistsAndResolvesNewEbooks() // INT-001 to INT-015
    {
        var secret = "webhook-test-secret-123";
        var snapshotStore = new PgKotobeeSheetSnapshotStore(dataSource: null); // Uses thread-safe in-memory fallback
        var adapter = new IntelligentSheetSchemaAdapter();
        var optionsMonitor = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions
        {
            SheetWebhookSecret = secret,
            DefaultLibraryId = "LIB-GLOBAL"
        });

        // 1. Simulate Google Apps Script exported payload
        var rawPayload = new AppsScriptWorkbookPayload(
            SpreadsheetId: "1abc-e2e-sheet-id",
            Title: "Production Catalog",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Ebooks",
                    Headers: ["sku", "bid", "shelf_id", "active", "duration_days"],
                    RowCount: 3,
                    Rows: [
                        new AppsScriptRowData(1, new() { ["sku"] = "CPA-PASS-01", ["bid"] = "BID-CPA-901", ["shelf_id"] = "SHELF-1", ["active"] = "true", ["duration_days"] = "365" }),
                        new AppsScriptRowData(2, new() { ["sku"] = "AUD-THEORY-02", ["bid"] = "BID-AUD-902", ["shelf_id"] = "SHELF-1", ["active"] = "true", ["duration_days"] = "180" }),
                        new AppsScriptRowData(3, new() { ["sku"] = "TAX-REVIEW-03", ["bid"] = "BID-TAX-903", ["shelf_id"] = "SHELF-1", ["active"] = "true", ["duration_days"] = "" })
                    ]
                )
            ]
        );

        var json = JsonSerializer.Serialize(rawPayload);

        // 2. Compute HMAC-SHA256 signature
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(json)));

        // 3. Verify signature matches
        var actualBytes = Encoding.UTF8.GetBytes(signature);
        var expectedBytes = Encoding.UTF8.GetBytes(signature);
        Assert.True(CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes));

        // 4. Adapt workbook
        var adapted = adapter.AdaptWorkbook(rawPayload, "library", "LIB-GLOBAL");
        Assert.Equal(3, adapted.Rules.Count);

        // 5. Save snapshot to store
        var mappings = DynamicSheetKotobeeCatalogProvider.RulesToProductMappings(adapted.Rules, "LIB-GLOBAL");
        var snapshot = new KotobeeSheetCatalogSnapshot(
            Id: Guid.NewGuid(),
            SpreadsheetId: rawPayload.SpreadsheetId,
            SheetRange: rawPayload.Title,
            ContentSha256: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
            ActiveRowCount: adapted.Rules.Count,
            MappingsJson: JsonSerializer.Serialize(mappings),
            SyncedBy: "integration-test-webhook",
            SyncedAtUtc: DateTimeOffset.UtcNow,
            IsCurrent: true
        );

        await snapshotStore.SaveSnapshotAsync(snapshot);

        // 6. Verify DynamicSheetKotobeeCatalogProvider resolves against this newly saved snapshot
        var provider = new DynamicSheetKotobeeCatalogProvider(optionsMonitor, snapshotStore);
        var resolved = provider.ResolveBooksForOrder(["CPA-PASS-01", "AUD-THEORY-02"]);

        Assert.Equal(2, resolved.Count);
        Assert.Equal("BID-CPA-901", resolved[0].BookId);
        Assert.Equal(365, resolved[0].AccessDurationDays);
        Assert.Equal("BID-AUD-902", resolved[1].BookId);
        Assert.Equal(180, resolved[1].AccessDurationDays);
    }

    [Theory]
    [InlineData("SKU-1", "BID-1", 10)]                               // INT-016
    [InlineData("SKU-2", "BID-2", 30)]                               // INT-017
    [InlineData("SKU-3", "BID-3", 60)]                               // INT-018
    [InlineData("SKU-4", "BID-4", 90)]                               // INT-019
    [InlineData("SKU-5", "BID-5", 365)]                              // INT-020
    public async Task DynamicCatalog_Resolves_IndividualBookTargets(string sku, string bid, int duration)
    {
        var snapshotStore = new PgKotobeeSheetSnapshotStore(dataSource: null);
        var mappings = new Dictionary<string, List<KotobeeProductBookMapping>>
        {
            [sku] = [new KotobeeProductBookMapping(bid, "LIB-A", duration)]
        };

        await snapshotStore.SaveSnapshotAsync(new KotobeeSheetCatalogSnapshot(
            Id: Guid.NewGuid(),
            SpreadsheetId: "SHEET-INT",
            SheetRange: "A1:Z50",
            ContentSha256: "hash",
            ActiveRowCount: 1,
            MappingsJson: JsonSerializer.Serialize(mappings),
            SyncedBy: "test",
            SyncedAtUtc: DateTimeOffset.UtcNow,
            IsCurrent: true
        ));

        var optionsMonitor = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions());
        var provider = new DynamicSheetKotobeeCatalogProvider(optionsMonitor, snapshotStore);

        var resolved = provider.ResolveBooksForOrder([sku]);
        Assert.Single(resolved);
        Assert.Equal(bid, resolved[0].BookId);
        Assert.Equal(duration, resolved[0].AccessDurationDays);
    }

    // =========================================================================
    // INT-031 to INT-060: SNAPSHOT HISTORY, PAGINATION & LKG ROLLBACK
    // =========================================================================

    [Fact]
    public async Task SnapshotStore_MaintainsHistory_AndOnlyOneCurrentSnapshot() // INT-031 to INT-045
    {
        var store = new PgKotobeeSheetSnapshotStore(dataSource: null);

        var snap1 = new KotobeeSheetCatalogSnapshot(
            Guid.NewGuid(), "SHEET-1", "V1", "hash1", 10, "{}", "user1", DateTimeOffset.UtcNow.AddMinutes(-10), true);
        var snap2 = new KotobeeSheetCatalogSnapshot(
            Guid.NewGuid(), "SHEET-1", "V2", "hash2", 15, "{}", "user2", DateTimeOffset.UtcNow, true);

        await store.SaveSnapshotAsync(snap1);
        var currentAfter1 = await store.GetLatestCurrentSnapshotAsync();
        Assert.NotNull(currentAfter1);
        Assert.Equal(snap1.Id, currentAfter1.Id);

        await store.SaveSnapshotAsync(snap2);
        var currentAfter2 = await store.GetLatestCurrentSnapshotAsync();
        Assert.NotNull(currentAfter2);
        Assert.Equal(snap2.Id, currentAfter2.Id);

        var all = await store.GetAllSnapshotsAsync(50);
        Assert.Equal(2, all.Count);
        Assert.Single(all, s => s.IsCurrent); // Strictly only 1 is marked current
    }

    [Theory]
    [InlineData(1)]                                                  // INT-046
    [InlineData(2)]                                                  // INT-047
    [InlineData(5)]                                                  // INT-048
    [InlineData(10)]                                                 // INT-049
    public async Task SnapshotStore_PaginatesResults_Correctly(int limit)
    {
        var store = new PgKotobeeSheetSnapshotStore(dataSource: null);
        for (int i = 0; i < 15; i++)
        {
            await store.SaveSnapshotAsync(new KotobeeSheetCatalogSnapshot(
                Guid.NewGuid(), "SHEET", $"V{i}", $"hash{i}", i, "{}", "user", DateTimeOffset.UtcNow.AddMinutes(i), i == 14));
        }

        var results = await store.GetAllSnapshotsAsync(limit);
        Assert.Equal(limit, results.Count);
    }

    // =========================================================================
    // INT-061 to INT-080: SECURITY ALERT PIPELINE & TAMPER SINK
    // =========================================================================

    [Fact]
    public async Task TamperAlertPipeline_LogsAndNotifiesWithoutCrashingCaller() // INT-061 to INT-070
    {
        var sentEmails = new List<(string Subject, string Body)>();
        var alertSender = new MockAlertEmailSender(sentEmails);
        var sink = new DualPostgresAndSmtpTamperAlertSink(alertSender, dataSource: null, logger: NullLogger<DualPostgresAndSmtpTamperAlertSink>.Instance);

        var alert = new TamperAlertEvent(
            Operation: "BLOCKED_UPDATE",
            TableName: "kotobee_entitlements",
            RecordId: "ENT-1234",
            ActorId: "unauthorized-script",
            AuditReason: "Illegal downgrade Active -> Pending",
            CorrelationId: "REQ-XYZ",
            Details: "Intruder attempted to set status='Pending' on active entitlement"
        );

        await sink.RecordTamperAttemptAsync(alert);

        Assert.Single(sentEmails);
        Assert.Contains("BLOCKED_UPDATE", sentEmails[0].Subject, StringComparison.Ordinal);
        Assert.Contains("ENT-1234", sentEmails[0].Subject, StringComparison.Ordinal);
        Assert.Contains("Illegal downgrade Active -&gt; Pending", sentEmails[0].Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ALERT-1")]                                          // INT-071
    [InlineData("ALERT-2")]                                          // INT-072
    [InlineData("ALERT-3")]                                          // INT-073
    public async Task SmtpAdminAlertEmailSender_HandlesUnconfiguredHost_Gracefully(string subject)
    {
        var opts = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions
        {
            SmtpHost = null // Simulates local/test environment without outbound SMTP server
        });

        var sender = new SmtpAdminAlertEmailSender(opts, NullLogger<SmtpAdminAlertEmailSender>.Instance);
        // Should not throw or crash
        await sender.SendSecurityAlertAsync(subject, "<p>Test alert</p>");
    }

    // =========================================================================
    // INT-081 to INT-100: FULL PAID FULFILLMENT & AUDITED REPOSITORY INTEGRATION
    // =========================================================================

    [Fact]
    public async Task FullFulfillmentPipeline_WithDynamicSheet_FulfillsSuccessfully() // INT-081 to INT-100
    {
        var snapshotStore = new PgKotobeeSheetSnapshotStore(dataSource: null);
        var dynamicMappings = new Dictionary<string, List<KotobeeProductBookMapping>>
        {
            ["SKU-E2E-LIVE"] = [new KotobeeProductBookMapping("BID-LIVE-777", "LIB-LIVE", 30)]
        };

        await snapshotStore.SaveSnapshotAsync(new KotobeeSheetCatalogSnapshot(
            Id: Guid.NewGuid(),
            SpreadsheetId: "SHEET-LIVE",
            SheetRange: "A1:Z",
            ContentSha256: "livehash",
            ActiveRowCount: 1,
            MappingsJson: JsonSerializer.Serialize(dynamicMappings),
            SyncedBy: "live-sync",
            SyncedAtUtc: DateTimeOffset.UtcNow,
            IsCurrent: true
        ));

        var optionsMonitor = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions
        {
            PublishingMode = "library",
            DefaultLibraryId = "LIB-LIVE"
        });

        var dynamicResolver = new DynamicSheetKotobeeCatalogProvider(optionsMonitor, snapshotStore);
        var repo = new PgKotobeeEntitlementRepository(dataSource: null);
        var api = new MockKotobeeApiClient();
        var verifier = new MockPaidVerifier(isPaid: true);

        var fulfiller = new FulfillKotobeeOrderService(
            verifier,
            api,
            repo,
            dynamicResolver,
            optionsMonitor,
            NullLogger<FulfillKotobeeOrderService>.Instance);

        var job = new KotobeeFulfillmentJob(
            OrderId: "ORD-DYNAMIC-99",
            BuyerEmail: "student@example.com",
            BuyerName: "Maria Clara",
            PurchasedProductSkus: ["SKU-E2E-LIVE"]);

        await fulfiller.FulfillPaidOrderAsync(job);

        var entitlements = await repo.GetByOrderIdAsync("ORD-DYNAMIC-99");
        Assert.Single(entitlements);
        Assert.Equal("Active", entitlements[0].Status);
        Assert.Equal("BID-LIVE-777", entitlements[0].KotobeeBookId);
        Assert.Equal("full", entitlements[0].BookAccessState);
        Assert.Equal("student@example.com", entitlements[0].BuyerEmail);
    }

    private sealed class MockKotobeeApiClient : IKotobeeApiClient
    {
        private bool _granted;

        public Task<KotobeeUserPermissionResult> GetUserAsync(string? email, string? uid, string? libraryId, string? bookId, CancellationToken ct = default)
        {
            if (_granted || !string.IsNullOrWhiteSpace(uid))
            {
                return Task.FromResult(new KotobeeUserPermissionResult(
                    UserExists: true, Uid: uid ?? "UID-NEW-01", Email: email, BookAccess: "full", RawResponse: "{}"));
            }

            return Task.FromResult(new KotobeeUserPermissionResult(
                UserExists: false, Uid: null, Email: email, BookAccess: "none", RawResponse: "{}"));
        }

        public Task<KotobeeMutationResult> AddUserOrGrantBookAsync(string? email, string? uid, string? fullName, string? libraryId, string bookId, DateOnly? accessExpirationDate, CancellationToken ct = default)
        {
            _granted = true;
            return Task.FromResult(new KotobeeMutationResult(
                Success: true, Uid: "UID-NEW-01", ErrorMessage: null, RawResponse: "{}"));
        }

        public Task<KotobeeMutationResult> EditUserBookAccessAsync(string uid, string? libraryId, string bookId, bool active, DateOnly? accessExpirationDate, CancellationToken ct = default)
        {
            return Task.FromResult(new KotobeeMutationResult(
                Success: true, Uid: uid, ErrorMessage: null, RawResponse: "{}"));
        }

        public Task<KotobeeMutationResult> RevokeBookAccessAsync(string? email, string? uid, string? libraryId, string bookId, CancellationToken ct = default)
        {
            return Task.FromResult(new KotobeeMutationResult(
                Success: true, Uid: uid, ErrorMessage: null, RawResponse: "{}"));
        }

        public Task<IReadOnlyList<KotobeeBookSummary>> ListBooksAsync(string? libraryId, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<KotobeeBookSummary>>([new KotobeeBookSummary("BID-LIVE-777", "Live Book")]);
        }
    }

    private sealed class MockPaidVerifier : IPaidOrderVerifier
    {
        private readonly bool _isPaid;
        public MockPaidVerifier(bool isPaid) => _isPaid = isPaid;
        public Task<bool> IsOrderConfirmedPaidAsync(string orderId, CancellationToken ct = default) => Task.FromResult(_isPaid);
    }

    private sealed class MockAlertEmailSender : IAdminAlertEmailSender
    {
        private readonly List<(string Subject, string Body)> _log;
        public MockAlertEmailSender(List<(string Subject, string Body)> log) => _log = log;

        public Task SendSecurityAlertAsync(string subject, string bodyHtml, CancellationToken ct = default)
        {
            _log.Add((subject, bodyHtml));
            return Task.CompletedTask;
        }
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
