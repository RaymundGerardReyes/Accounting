using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rge.Receipts.Application.Ports;

namespace Rge.Receipts.Infrastructure.Mail;

public sealed class ReceiptEmailBackgroundWorker : BackgroundService
{
    private readonly IReceiptEmailQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptEmailBackgroundWorker> _logger;

    public ReceiptEmailBackgroundWorker(
        IReceiptEmailQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ReceiptEmailBackgroundWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Receipt email background worker started.");

        await foreach (ReceiptEmailJob job in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var mailer = scope.ServiceProvider.GetRequiredService<IReceiptMailer>();

                var result = await mailer
                    .SendReceiptEmailAsync(job.Receipt, job.RecipientEmail, job.PdfBytes, stoppingToken)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    _logger.LogWarning(
                        "Receipt email delivery failed for receipt {ReceiptNumber} to {Recipient}: {ErrorCode} - {ErrorMessage}",
                        job.Receipt.ReceiptNumber.Value,
                        job.RecipientEmail,
                        result.Error.Code,
                        result.Error.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception dispatching receipt email for receipt {ReceiptNumber} to {Recipient}",
                    job.Receipt.ReceiptNumber.Value,
                    job.RecipientEmail);
            }
        }

        _logger.LogInformation("Receipt email background worker stopped.");
    }
}
