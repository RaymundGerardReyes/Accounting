using System.Diagnostics;



namespace Rge.BuildingBlocks.Infrastructure.Observability;



/// <summary>

/// Provides allocation-friendly structured logging scope properties linking

/// TraceId, CorrelationId, and ActorId across every log line and audit event.

/// </summary>

public static class LogEnrichers

{

    public static KeyValuePair<string, object?>[] CreateRequestScope(

        string correlationId,

        string? actorId = null)

    {

        string traceId = Activity.Current?.TraceId.ToString() ?? correlationId;

        return

        [

            new KeyValuePair<string, object?>("correlation_id", correlationId),

            new KeyValuePair<string, object?>("trace_id", traceId),

            new KeyValuePair<string, object?>("actor_id", actorId ?? "anonymous")

        ];

    }

}
