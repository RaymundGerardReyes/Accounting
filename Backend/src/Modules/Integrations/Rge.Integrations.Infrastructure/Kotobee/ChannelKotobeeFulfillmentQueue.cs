using System.Threading.Channels;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

public sealed class ChannelKotobeeFulfillmentQueue : IKotobeeFulfillmentQueue
{
    private readonly Channel<KotobeeFulfillmentJob> _channel =
        Channel.CreateBounded<KotobeeFulfillmentJob>(new BoundedChannelOptions(1000)
        {
            SingleReader = true, // Guarantees sequential execution for 1 req/sec rate limit
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    public ValueTask EnqueueAsync(KotobeeFulfillmentJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<KotobeeFulfillmentJob> DequeueAllAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAllAsync(ct);
}
