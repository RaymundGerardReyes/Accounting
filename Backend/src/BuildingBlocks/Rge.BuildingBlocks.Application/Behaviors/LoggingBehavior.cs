using System.Diagnostics;

using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Application.Behaviors;



/// <summary>

/// Measures handler execution latency using zero-allocation Stopwatch.GetTimestamp()

/// and enriches the active OpenTelemetry Activity span.

/// </summary>

public static class LoggingBehavior

{

    public static async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(

        TRequest request,

        ICurrentActor currentActor,

        RequestHandlerDelegate<TResponse> next,

        Action<string, string, double, bool, string?>? onCompletedLog = null,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(currentActor);

        ArgumentNullException.ThrowIfNull(next);



        string requestName = typeof(TRequest).Name;

        long startTimestamp = Stopwatch.GetTimestamp();



        Activity? activity = Activity.Current;

        activity?.SetTag("rge.request.name", requestName);

        activity?.SetTag("rge.actor.id", currentActor.ActorId);

        activity?.SetTag("rge.correlation.id", currentActor.CorrelationId);



        Result<TResponse> result = await next(cancellationToken).ConfigureAwait(false);



        double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        activity?.SetTag("rge.request.duration_ms", elapsedMs);

        activity?.SetTag("rge.request.succeeded", result.IsSuccess);



        onCompletedLog?.Invoke(

            requestName,

            currentActor.CorrelationId,

            elapsedMs,

            result.IsSuccess,

            result.IsFailure ? result.Error.Code : null);



        return result;

    }

}
