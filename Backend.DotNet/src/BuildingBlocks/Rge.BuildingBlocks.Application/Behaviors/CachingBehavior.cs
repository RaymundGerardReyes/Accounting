using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Application.Behaviors;



public interface IApplicationCache

{

    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default);

}



/// <summary>

/// Serves read-heavy queries (policies, approval matrix, product catalog) from L1/HybridCache

/// to guarantee < 30 ms read latency.

/// </summary>

public static class CachingBehavior

{

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);



    public static async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(

        TRequest request,

        IApplicationCache cache,

        RequestHandlerDelegate<TResponse> next,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(cache);

        ArgumentNullException.ThrowIfNull(next);



        if (request is not ICacheableQuery cacheable || string.IsNullOrWhiteSpace(cacheable.CacheKey))

        {

            return await next(cancellationToken).ConfigureAwait(false);

        }



        TResponse? cached = await cache.GetAsync<TResponse>(cacheable.CacheKey, cancellationToken).ConfigureAwait(false);

        if (cached is not null)

        {

            return Result.Success(cached);

        }



        Result<TResponse> result = await next(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess && result.Value is not null)

        {

            await cache.SetAsync(

                cacheable.CacheKey,

                result.Value,

                cacheable.Expiration ?? DefaultTtl,

                cancellationToken).ConfigureAwait(false);

        }



        return result;

    }

}
