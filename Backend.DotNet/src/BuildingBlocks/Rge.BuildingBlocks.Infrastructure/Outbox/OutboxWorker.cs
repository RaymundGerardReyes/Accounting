using System.Threading.Channels;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Asynchronous background outbox processor combining a bounded System.Threading.Channel
/// wake-up signal (for immediate sub-millisecond dispatch after webhook intake)
/// with periodic fallback polling every 5 seconds.
/// </summary>
public sealed class OutboxWorker : IDisposable
{
    private readonly Channel<bool> _wakeupChannel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly Func<IOutboxStore> _storeFactory;
    private readonly OutboxDispatcher _dispatcher;
    private readonly OutboxLeaseManager _leaseManager;
    private readonly IClock _clock;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _executingTask;

    public OutboxWorker(
        Func<IOutboxStore> storeFactory,
        OutboxDispatcher dispatcher,
        OutboxLeaseManager leaseManager,
        IClock clock,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(storeFactory);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(leaseManager);
        ArgumentNullException.ThrowIfNull(clock);

        _storeFactory = storeFactory;
        _dispatcher = dispatcher;
        _leaseManager = leaseManager;
        _clock = clock;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Signals the background loop immediately after a webhook or command writes to the outbox.
    /// </summary>
    public void SignalImmediateWakeup() =>
        _wakeupChannel.Writer.TryWrite(true);

    /// <summary>
    /// Starts the continuous background consumer loop.
    /// </summary>
    public void StartBackgroundLoop(CancellationToken hostStoppingToken = default)
    {
        if (_executingTask is not null)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(hostStoppingToken);
        _executingTask = Task.Run(() => RunContinuousLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Waits for an immediate wake-up signal or until <paramref name="fallbackDelay"/> elapses.
    /// </summary>
    public async ValueTask<bool> WaitForWakeupSignalAsync(
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(fallbackDelay);

        try
        {
            if (await _wakeupChannel.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                while (_wakeupChannel.Reader.TryRead(out _))
                {
                }
                return true;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Fallback polling interval elapsed normally
        }

        return false;
    }

    private async Task RunContinuousLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain all pending batches until the queue is empty
                int processed;
                do
                {
                    processed = await ProcessNextBatchOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                while (processed > 0 && !stoppingToken.IsCancellationRequested);

                // Wait for either an immediate channel wake-up signal OR the periodic fallback timer
                Task<bool> channelWait = _wakeupChannel.Reader.WaitToReadAsync(stoppingToken).AsTask();
                Task<bool> timerWait = timer.WaitForNextTickAsync(stoppingToken).AsTask();

                await Task.WhenAny(channelWait, timerWait).ConfigureAwait(false);

                while (_wakeupChannel.Reader.TryRead(out _))
                {
                    // Clear coalesced wake-up signals
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Back off briefly on transient DB connectivity errors
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<int> ProcessNextBatchOnceAsync(CancellationToken cancellationToken = default)
    {
        IOutboxStore store = _storeFactory();
        DateTimeOffset nowUtc = _clock.UtcNow;

        IReadOnlyList<OutboxMessage> messages = await store.ClaimBatchAsync(
            _leaseManager.WorkerId,
            _leaseManager.BatchSize,
            _leaseManager.LeaseDuration,
            nowUtc,
            cancellationToken).ConfigureAwait(false);

        int processedCount = 0;
        foreach (OutboxMessage msg in messages)
        {
            try
            {
                await _dispatcher.DispatchAsync(msg, cancellationToken).ConfigureAwait(false);
                await store.MarkCompletedAsync(msg.Id, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
                processedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await store.MarkFailedAsync(msg.Id, ex.Message, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
            }
        }

        return processedCount;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
