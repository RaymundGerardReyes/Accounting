using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rge.Receipts.Application.Ports;

namespace Rge.Receipts.Infrastructure.Mail;

[SuppressMessage("Performance", "CA1873:Avoid unnecessary evaluation of arguments in logging", Justification = "Receipt properties are fast lightweight string getters")]
public sealed class ReceiptEmailBackgroundWorker : BackgroundService
{
    private const int WorkerConcurrency = 3;
    private const int MaxDeliveryAttempts = 2;
    private static readonly TimeSpan PerEmailTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(750);

    private readonly IReceiptEmailQueue _queue;
    private readonly ChannelReceiptEmailQueue? _channelQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptEmailBackgroundWorker> _logger;

    public ReceiptEmailBackgroundWorker(
        ChannelReceiptEmailQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ReceiptEmailBackgroundWorker> logger)
    {
        _queue = queue;
        _channelQueue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ReceiptEmailBackgroundWorker(
        IReceiptEmailQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ReceiptEmailBackgroundWorker> logger)
    {
        _queue = queue;
        _channelQueue = queue as ChannelReceiptEmailQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Spawn bounded concurrent consumers so one slow SMTP handshake never blocks other receipts
            var consumers = Enumerable.Range(1, WorkerConcurrency)
                .Select(workerId => ConsumeQueueLoopAsync(workerId, stoppingToken))
                .ToArray();

            await Task.WhenAll(consumers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[RECEIPT-WORKER-STOPPED] Receipt email background worker stopped gracefully.");
        }
    }

    private async Task ConsumeQueueLoopAsync(int workerId, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessEmailJobWithRetryAsync(workerId, job, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown: stoppingToken signaled cancellation while reading from channel
        }
    }

    private async Task ProcessEmailJobWithRetryAsync(
        int workerId,
        ReceiptEmailJob job,
        CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxDeliveryAttempts; attempt++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(PerEmailTimeout);

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var mailer = scope.ServiceProvider.GetRequiredService<IReceiptMailer>();
                var pdfRenderer = scope.ServiceProvider.GetService<IPdfRenderer>();

                var recipient = !string.IsNullOrWhiteSpace(job.RecipientEmail)
                    ? job.RecipientEmail.Trim()
                    : job.Receipt.CustomerEmail;

                var pdfBytes = (job.PdfBytes is { Length: > 0 })
                    ? job.PdfBytes
                    : (pdfRenderer is not null
                        ? await pdfRenderer.RenderAsync(job.Receipt, timeoutCts.Token).ConfigureAwait(false)
                        : []);

                await mailer
                    .SendReceiptEmailAsync(job.Receipt, recipient, pdfBytes, timeoutCts.Token)
                    .ConfigureAwait(false);

                _channelQueue?.MarkCompleted(job.Receipt);

                _logger.LogInformation(
                    "[RECEIPT-EMAIL-SENT] worker={WorkerId} | receipt_no={ReceiptNumber} | payment_id={PaymentId} | to={Recipient} | attempt={Attempt}",
                    workerId,
                    job.Receipt.ReceiptNumber.Value,
                    job.Receipt.PaymentId,
                    recipient,
                    attempt);

                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= MaxDeliveryAttempts)
                {
                    _channelQueue?.ClearDeduplicationKey(job.Receipt);
                    _logger.LogWarning(
                        ex,
                        "[RECEIPT-EMAIL-DROPPED] worker={WorkerId} | receipt_no={ReceiptNumber} | to={Recipient} failed after {MaxAttempts} attempts.",
                        workerId,
                        job.Receipt.ReceiptNumber.Value,
                        job.Receipt.CustomerEmail,
                        MaxDeliveryAttempts);
                    return;
                }

                _logger.LogDebug(
                    ex,
                    "[RECEIPT-EMAIL-RETRY] worker={WorkerId} | receipt_no={ReceiptNumber} transient error on attempt {Attempt}; retrying.",
                    workerId,
                    job.Receipt.ReceiptNumber.Value,
                    attempt);

                try
                {
                    await Task.Delay(RetryBackoff, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
