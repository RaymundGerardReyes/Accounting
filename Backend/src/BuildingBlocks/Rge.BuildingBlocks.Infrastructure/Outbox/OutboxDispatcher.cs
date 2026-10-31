using System.Collections.Concurrent;

using Rge.BuildingBlocks.Application.Messaging;



namespace Rge.BuildingBlocks.Infrastructure.Outbox;



public interface IOutboxEventHandler

{

    string EventType { get; }

    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default);

}



/// <summary>

/// Routes claimed OutboxMessage records in-process to registered module handlers

/// (e.g., OrderPaid -> IssueReceipt, RenderPdf, SendSmtpEmail, SyncGoogleSheet).

/// </summary>

public sealed class OutboxDispatcher

{

    private readonly ConcurrentDictionary<string, List<IOutboxEventHandler>> _handlers =

        new(StringComparer.OrdinalIgnoreCase);



    public void RegisterHandler(IOutboxEventHandler handler)

    {

        ArgumentNullException.ThrowIfNull(handler);

        List<IOutboxEventHandler> list = _handlers.GetOrAdd(handler.EventType, static _ => []);

        lock (list)

        {

            list.Add(handler);

        }

    }



    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(message);



        if (!_handlers.TryGetValue(message.EventType, out List<IOutboxEventHandler>? handlers))

        {

            return;

        }



        IOutboxEventHandler[] snapshot;

        lock (handlers)

        {

            snapshot = [.. handlers];

        }



        foreach (IOutboxEventHandler handler in snapshot)

        {

            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);

        }

    }

}
