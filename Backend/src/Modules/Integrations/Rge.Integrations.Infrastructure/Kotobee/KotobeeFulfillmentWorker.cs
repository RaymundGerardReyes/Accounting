using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

public sealed class KotobeeFulfillmentWorker : BackgroundService
{
    private readonly IKotobeeFulfillmentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KotobeeFulfillmentWorker> _logger;

    public KotobeeFulfillmentWorker(
        IKotobeeFulfillmentQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<KotobeeFulfillmentWorker> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kotobee Fulfillment Worker background service started.");

        try
        {
            await foreach (var job in _queue.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var fulfiller = scope.ServiceProvider.GetRequiredService<FulfillKotobeeOrderService>();
                    await fulfiller.FulfillPaidOrderAsync(job, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Unhandled error processing Kotobee fulfillment for Order {OrderId}.", job.OrderId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Kotobee Fulfillment Worker stopping gracefully.");
        }
    }
}
