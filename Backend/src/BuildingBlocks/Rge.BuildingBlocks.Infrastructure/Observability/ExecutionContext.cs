using System;
using System.Diagnostics;
using System.Threading;

namespace Rge.BuildingBlocks.Infrastructure.Observability;

public sealed record ExecutionMetadata(
    string CorrelationId,
    string TraceId,
    string SpanId,
    string ExecutionOrigin,
    string? HttpMethod = null,
    string? HttpRoute = null,
    string? ActorId = null,
    string? ActorRole = null,
    string? ModuleName = null,
    string? HandlerName = null,
    string? OperationName = null,
    string? OperationType = null,
    string? TransactionId = null);

public interface IExecutionContextAccessor
{
    ExecutionMetadata Current { get; }
    IDisposable BeginScope(Func<ExecutionMetadata, ExecutionMetadata> updater);
}

public sealed class ExecutionContextAccessor : IExecutionContextAccessor
{
    private static readonly AsyncLocal<ExecutionMetadata?> CurrentMetadata = new();

    public static ExecutionContextAccessor Instance { get; } = new();

    public ExecutionMetadata Current
    {
        get
        {
            var activity = Activity.Current;
            var traceId = activity?.TraceId.ToString() ?? "no-trace";
            var spanId = activity?.SpanId.ToString() ?? "no-span";

            var existing = CurrentMetadata.Value;
            if (existing is not null)
            {
                return existing with
                {
                    TraceId = traceId,
                    SpanId = spanId
                };
            }

            return new ExecutionMetadata(
                CorrelationId: activity?.GetTagItem("correlation.id")?.ToString() ?? Guid.NewGuid().ToString("N"),
                TraceId: traceId,
                SpanId: spanId,
                ExecutionOrigin: "System");
        }
    }

    public IDisposable BeginScope(Func<ExecutionMetadata, ExecutionMetadata> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        var previous = CurrentMetadata.Value;
        var baseline = previous ?? Current;
        var next = updater(baseline);
        CurrentMetadata.Value = next;

        return new ScopeResetter(previous);
    }

    private sealed class ScopeResetter : IDisposable
    {
        private readonly ExecutionMetadata? _previous;
        private int _disposed;

        public ScopeResetter(ExecutionMetadata? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                CurrentMetadata.Value = _previous;
            }
        }
    }
}
