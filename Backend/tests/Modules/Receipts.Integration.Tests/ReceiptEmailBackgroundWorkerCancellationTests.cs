using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rge.Receipts.Infrastructure.Mail;
using Xunit;

namespace Rge.Receipts.Integration.Tests;

/// <summary>
/// Guardrail R3: Verifies that ReceiptEmailBackgroundWorker shuts down gracefully
/// when the cancellation token or StopAsync is signaled, without unhandled exceptions or faulted tasks.
/// </summary>
public sealed class ReceiptEmailBackgroundWorkerCancellationTests
{
    [Fact]
    public async Task Worker_OnStopAsyncTriggered_ShutsDownGracefullyWithoutThrowing()
    {
        // Arrange
        var queue = new ChannelReceiptEmailQueue(capacity: 10);
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = NullLogger<ReceiptEmailBackgroundWorker>.Instance;

        using var worker = new ReceiptEmailBackgroundWorker(queue, scopeFactory, logger);

        // Act: Start worker with an active token
        await worker.StartAsync(CancellationToken.None);

        // Let the 3 consumer loops enter ReadAllAsync
        await Task.Delay(50);

        // Signal stop (internally cancels stoppingToken)
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StopAsync(stopCts.Token);

        // Assert: ExecuteTask completes cleanly (not faulted with unhandled exception)
        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask;
        Assert.True(worker.ExecuteTask.IsCompleted);
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        Assert.False(worker.ExecuteTask.IsFaulted, $"ExecuteTask faulted with: {worker.ExecuteTask.Exception}");
    }

    [Fact]
    public async Task Worker_OnStartupCancellationTokenCanceled_TerminatesCleanly()
    {
        // Arrange
        var queue = new ChannelReceiptEmailQueue(capacity: 10);
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = NullLogger<ReceiptEmailBackgroundWorker>.Instance;

        using var worker = new ReceiptEmailBackgroundWorker(queue, scopeFactory, logger);
        using var cts = new CancellationTokenSource();

        // Act: Start worker with cts.Token and immediately cancel
        await worker.StartAsync(cts.Token);
        await cts.CancelAsync();

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StopAsync(stopCts.Token);

        // Assert: ExecuteTask completes gracefully without unhandled exception
        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask;
        Assert.True(worker.ExecuteTask.IsCompleted);
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }
}
