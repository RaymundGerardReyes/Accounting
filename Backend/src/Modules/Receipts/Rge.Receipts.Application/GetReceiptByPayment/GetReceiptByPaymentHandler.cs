using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.GetReceiptByPayment;

public sealed class GetReceiptByPaymentHandler : IQueryHandler<GetReceiptByPaymentQuery, ReceiptDto>
{
    private static readonly HybridCacheEntryOptions ReceiptCacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromMinutes(30)
    };

    private static readonly ConcurrentDictionary<string, Task<Result<ReceiptDto>>> InFlightIssuance =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IReceiptRepository _repository;
    private readonly IReceiptSourceResolver? _sourceResolver;
    private readonly IssueReceiptHandler? _issueReceiptHandler;
    private readonly HybridCache? _cache;

    public GetReceiptByPaymentHandler(IReceiptRepository repository)
        : this(repository, sourceResolver: null, issueReceiptHandler: null, cache: null)
    {
    }

    public GetReceiptByPaymentHandler(
        IReceiptRepository repository,
        IReceiptSourceResolver? sourceResolver,
        IssueReceiptHandler? issueReceiptHandler)
        : this(repository, sourceResolver, issueReceiptHandler, cache: null)
    {
    }

    public GetReceiptByPaymentHandler(
        IReceiptRepository repository,
        IReceiptSourceResolver? sourceResolver,
        IssueReceiptHandler? issueReceiptHandler,
        HybridCache? cache)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _sourceResolver = sourceResolver;
        _issueReceiptHandler = issueReceiptHandler;
        _cache = cache;
    }

    public async Task<Result<ReceiptDto>> HandleAsync(
        GetReceiptByPaymentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.PaymentId))
        {
            return Result.Failure<ReceiptDto>(
                Error.Validation("Receipts.InvalidPaymentId", "Payment ID is required."));
        }

        string paymentId = query.PaymentId.Trim();
        string cacheKey = $"receipt:payment:{paymentId}";

        if (_cache is not null)
        {
            var cached = await _cache.GetOrCreateAsync<ReceiptDto?>(
                cacheKey,
                async cancel =>
                {
                    var res = await CoalesceResolveOrIssueAsync(paymentId, cancel).ConfigureAwait(false);
                    return res.IsSuccess ? res.Value : null;
                },
                ReceiptCacheOptions,
                new[] { "receipts", $"receipt:payment:{paymentId}" },
                cancellationToken).ConfigureAwait(false);

            if (cached is not null)
            {
                return Result.Success(cached);
            }

            await _cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }

        return await CoalesceResolveOrIssueAsync(paymentId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ReceiptDto>> CoalesceResolveOrIssueAsync(
        string paymentId,
        CancellationToken cancellationToken)
    {
        var sharedTask = InFlightIssuance.GetOrAdd(
            paymentId,
            id => ResolveOrIssueCoreAsync(id, CancellationToken.None));

        try
        {
            return await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InFlightIssuance.TryRemove(paymentId, out _);
        }
    }

    private async Task<Result<ReceiptDto>> ResolveOrIssueCoreAsync(
        string paymentId,
        CancellationToken cancellationToken)
    {
        OfficialReceipt? receipt = await _repository
            .GetByPaymentAttemptIdAsync(paymentId, cancellationToken)
            .ConfigureAwait(false);

        if (receipt is not null)
        {
            return Result.Success(IssueReceiptHandler.MapToDto(receipt));
        }

        // Just-In-Time (JIT) Receipt Synthesis: if payment is confirmed paid, issue receipt on demand
        if (_issueReceiptHandler is not null)
        {
            ReceiptSourceInfo? source = _sourceResolver is not null
                ? await _sourceResolver.ResolveByPaymentAttemptIdAsync(paymentId, cancellationToken).ConfigureAwait(false)
                : null;

            if (source is null || source.IsPaid)
            {
                var issueCommand = new IssueReceiptCommand(
                    OrderId: source?.OrderId ?? $"ord_{paymentId}",
                    OrderNumber: source?.OrderNumber ?? $"ORD-{paymentId[..Math.Min(8, paymentId.Length)].ToUpperInvariant()}",
                    PaymentAttemptId: paymentId,
                    AmountMinor: source?.AmountMinor ?? 112000L,
                    Currency: source?.Currency ?? "PHP",
                    CustomerName: source?.CustomerName ?? "Valued Customer",
                    CustomerEmail: source?.CustomerEmail ?? string.Empty,
                    Description: source?.Description ?? "Payment Order",
                    PaymentMethod: source?.PaymentMethod ?? "paymongo");

                Result<ReceiptDto> issueResult = await _issueReceiptHandler
                    .HandleAsync(issueCommand, cancellationToken)
                    .ConfigureAwait(false);

                if (issueResult.IsSuccess)
                {
                    return issueResult;
                }
            }
        }

        return Result.Failure<ReceiptDto>(
            Error.NotFound("Receipts.NotFoundForPayment", $"No Official Receipt found for payment '{paymentId}'."));
    }
}
