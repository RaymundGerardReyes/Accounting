using Microsoft.Extensions.Hosting;
using Rge.BuildingBlocks.Infrastructure.Outbox;

namespace Rge.Accounting.Host.Composition;

/// <summary>
/// Hosted background service that activates the <see cref="OutboxWorker"/> continuous consumer loop,
/// processing and dispatching queued integration events (e.g. order.paid -> BIR Official Receipt)
/// off the request thread.
/// </summary>
public sealed class OutboxHostedService : BackgroundService
{
    private readonly OutboxWorker _worker;

    public OutboxHostedService(OutboxWorker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        _worker = worker;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _worker.StartBackgroundLoop(stoppingToken);
        return Task.CompletedTask;
    }
}

