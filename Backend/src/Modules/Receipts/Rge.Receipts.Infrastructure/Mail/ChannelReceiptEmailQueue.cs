using System.Threading.Channels;
using Rge.Receipts.Application.Ports;

namespace Rge.Receipts.Infrastructure.Mail;

public sealed class ChannelReceiptEmailQueue : IReceiptEmailQueue
{
    private readonly Channel<ReceiptEmailJob> _channel;

    public ChannelReceiptEmailQueue(int capacity = 10_000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<ReceiptEmailJob>(options);
    }

    public ValueTask EnqueueAsync(ReceiptEmailJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public IAsyncEnumerable<ReceiptEmailJob> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
