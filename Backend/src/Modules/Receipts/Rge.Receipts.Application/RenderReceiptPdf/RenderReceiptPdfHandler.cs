using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.RenderReceiptPdf;

public sealed class RenderReceiptPdfHandler :
    IHandler<RenderReceiptPdfQuery, RenderReceiptPdfResponse>,
    IQueryHandler<RenderReceiptPdfQuery, RenderReceiptPdfResponse>,
    IHandler<RenderReceiptPdfQuery, RenderedReceiptPdfDto>,
    IQueryHandler<RenderReceiptPdfQuery, RenderedReceiptPdfDto>
{
    private static readonly HybridCacheEntryOptions PdfCacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromMinutes(30)
    };

    private static readonly ConcurrentDictionary<string, Task<Result<RenderReceiptPdfResponse>>> InFlightPdfRequests =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IReceiptRepository _receipts;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly IHandler<IssueReceiptCommand, IssueReceiptResponse>? _issueHandler;
    private readonly HybridCache? _cache;
    private readonly IReceiptSourceResolver? _sourceResolver;

    public RenderReceiptPdfHandler(
        IReceiptRepository receipts,
        IPdfRenderer pdfRenderer)
        : this(receipts, pdfRenderer, issueHandler: null, cache: null, sourceResolver: null)
    {
    }

    public RenderReceiptPdfHandler(
        IReceiptRepository receipts,
        IPdfRenderer pdfRenderer,
        IHandler<IssueReceiptCommand, IssueReceiptResponse>? issueHandler,
        HybridCache? cache = null)
        : this(receipts, pdfRenderer, issueHandler, cache, sourceResolver: null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public RenderReceiptPdfHandler(
        IReceiptRepository receipts,
        IPdfRenderer pdfRenderer,
        IReceiptSourceResolver? sourceResolver,
        IssueReceiptHandler? issueReceiptHandler,
        HybridCache? cache = null)
        : this(receipts, pdfRenderer, (IHandler<IssueReceiptCommand, IssueReceiptResponse>?)issueReceiptHandler, cache, sourceResolver)
    {
    }

    public RenderReceiptPdfHandler(
        IReceiptRepository receipts,
        IPdfRenderer pdfRenderer,
        IHandler<IssueReceiptCommand, IssueReceiptResponse>? issueHandler,
        HybridCache? cache,
        IReceiptSourceResolver? sourceResolver)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(pdfRenderer);

        _receipts = receipts;
        _pdfRenderer = pdfRenderer;
        _issueHandler = issueHandler;
        _cache = cache;
        _sourceResolver = sourceResolver;
    }

    public async Task<Result<RenderReceiptPdfResponse>> HandleAsync(
        RenderReceiptPdfQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var paymentId = request.PaymentId?.Trim();
        var orderId = request.OrderId?.Trim();
        var lookupKey = !string.IsNullOrEmpty(paymentId) ? paymentId : orderId;

        if (string.IsNullOrWhiteSpace(lookupKey))
        {
            return Result.Failure<RenderReceiptPdfResponse>(
                Error.Validation("ReceiptPdf.InvalidPaymentId", "Payment ID or Order ID is required to render Official Receipt PDF."));
        }

        var cacheKey = $"receipt:pdf:payment:{lookupKey}";

        if (_cache is not null)
        {
            var cached = await _cache.GetOrCreateAsync<RenderReceiptPdfResponse?>(
                cacheKey,
                async cancel =>
                {
                    var res = await CoalesceRenderAsync(lookupKey, paymentId, orderId, cancel).ConfigureAwait(false);
                    return res.IsSuccess ? res.Value : null;
                },
                PdfCacheOptions,
                new[] { "receipts", "receipts:pdf", cacheKey },
                cancellationToken).ConfigureAwait(false);

            if (cached is not null && cached.PdfBytes is { Length: > 0 })
            {
                return Result.Success(cached);
            }

            await _cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }

        return await CoalesceRenderAsync(lookupKey, paymentId, orderId, cancellationToken).ConfigureAwait(false);
    }

    async Task<Result<RenderedReceiptPdfDto>> IHandler<RenderReceiptPdfQuery, RenderedReceiptPdfDto>.HandleAsync(
        RenderReceiptPdfQuery request,
        CancellationToken cancellationToken)
    {
        var res = await HandleAsync(request, cancellationToken).ConfigureAwait(false);
        if (res.IsFailure)
        {
            return Result.Failure<RenderedReceiptPdfDto>(res.Error);
        }

        return Result.Success(new RenderedReceiptPdfDto(
            res.Value.FileName,
            res.Value.ContentType,
            res.Value.PdfBytes));
    }

    private async Task<Result<RenderReceiptPdfResponse>> CoalesceRenderAsync(
        string lookupKey,
        string? paymentId,
        string? orderId,
        CancellationToken ct)
    {
        var sharedTask = InFlightPdfRequests.GetOrAdd(
            lookupKey,
            _ => ResolveAndRenderCoreAsync(lookupKey, paymentId, orderId, CancellationToken.None));

        try
        {
            return await sharedTask.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            InFlightPdfRequests.TryRemove(lookupKey, out _);
        }
    }

    private async Task<Result<RenderReceiptPdfResponse>> ResolveAndRenderCoreAsync(
        string lookupKey,
        string? paymentId,
        string? orderId,
        CancellationToken ct)
    {
        OfficialReceipt? receipt = null;
        if (!string.IsNullOrWhiteSpace(paymentId))
        {
            receipt = await _receipts.GetByPaymentIdAsync(paymentId, ct).ConfigureAwait(false)
                ?? await _receipts.GetByPaymentAttemptIdAsync(paymentId, ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(orderId))
        {
            receipt = await _receipts.GetByOrderIdAsync(orderId, ct).ConfigureAwait(false);
        }

        if (receipt is null && _issueHandler is not null)
        {
            var issueCmd = !string.IsNullOrWhiteSpace(paymentId)
                ? new IssueReceiptCommand(paymentId)
                : new IssueReceiptCommand(orderId!);

            var issueResult = await _issueHandler
                .HandleAsync(issueCmd, ct)
                .ConfigureAwait(false);

            if (issueResult.IsFailure)
            {
                return Result.Failure<RenderReceiptPdfResponse>(issueResult.Error);
            }

            receipt = !string.IsNullOrWhiteSpace(paymentId)
                ? (await _receipts.GetByPaymentIdAsync(paymentId, ct).ConfigureAwait(false)
                    ?? await _receipts.GetByPaymentAttemptIdAsync(paymentId, ct).ConfigureAwait(false))
                : await _receipts.GetByOrderIdAsync(orderId!, ct).ConfigureAwait(false);
        }

        if (receipt is null && _sourceResolver is not null && _issueHandler is not null)
        {
            ReceiptSourceInfo? source = !string.IsNullOrWhiteSpace(paymentId)
                ? await _sourceResolver.ResolveByPaymentAttemptIdAsync(paymentId, ct).ConfigureAwait(false)
                : await _sourceResolver.ResolveByOrderIdAsync(orderId!, ct).ConfigureAwait(false);

            if (source is not null && source.IsPaid)
            {
                var issueCommand = new IssueReceiptCommand(
                    OrderId: source.OrderId,
                    OrderNumber: source.OrderNumber,
                    PaymentAttemptId: source.PaymentAttemptId,
                    AmountMinor: source.AmountMinor,
                    Currency: source.Currency,
                    CustomerName: source.CustomerName,
                    CustomerEmail: source.CustomerEmail,
                    Description: source.Description,
                    PaymentMethod: source.PaymentMethod);

                var issueResult = await _issueHandler.HandleAsync(issueCommand, ct).ConfigureAwait(false);
                if (issueResult.IsSuccess)
                {
                    receipt = await _receipts.GetByPaymentAttemptIdAsync(source.PaymentAttemptId, ct).ConfigureAwait(false)
                        ?? await _receipts.GetByOrderIdAsync(source.OrderId, ct).ConfigureAwait(false);
                }
            }
        }

        if (receipt is null)
        {
            return Result.Failure<RenderReceiptPdfResponse>(
                Error.NotFound("Receipt.NotFound", $"Official receipt for '{lookupKey}' was not found."));
        }

        var pdfBytes = await _pdfRenderer.RenderAsync(receipt, ct).ConfigureAwait(false);
        var fileName = $"{receipt.ReceiptNumber.Value}.pdf";

        return Result.Success(new RenderReceiptPdfResponse(
            ReceiptId: receipt.Id,
            ReceiptNumber: receipt.ReceiptNumber.Value,
            FileName: fileName,
            ContentType: "application/pdf",
            PdfBytes: pdfBytes,
            AuditDigest: receipt.AuditDigest.Value));
    }
}
