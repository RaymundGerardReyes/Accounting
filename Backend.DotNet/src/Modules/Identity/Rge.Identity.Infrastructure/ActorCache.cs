using System.Collections.Concurrent;

using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.Identity.Application.Ports;

using Rge.Identity.Domain;



namespace Rge.Identity.Infrastructure;



/// <summary>

/// Thread-safe L1 in-memory cache for resolved Actor aggregates and their RBAC role assignments.

/// Cuts read latency on authenticated requests and supports instant invalidation on role changes.

/// </summary>

public sealed class ActorCache : IActorCache

{

    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);



    private sealed record CacheEntry(Actor Actor, DateTimeOffset ExpiresAtUtc);



    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private readonly IClock _clock;

    private readonly TimeSpan _ttl;



    public ActorCache(IClock clock, TimeSpan? ttl = null)

    {

        _clock = clock;

        _ttl = ttl ?? DefaultCacheTtl;

    }



    public async Task<Actor?> GetOrLoadActorAsync(

        string userId,

        Func<CancellationToken, Task<Actor?>> loader,

        CancellationToken cancellationToken = default)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        ArgumentNullException.ThrowIfNull(loader);



        DateTimeOffset nowUtc = _clock.UtcNow;



        if (_entries.TryGetValue(userId, out CacheEntry? existing) && existing.ExpiresAtUtc > nowUtc)

        {

            return existing.Actor;

        }



        Actor? loaded = await loader(cancellationToken).ConfigureAwait(false);

        if (loaded is not null && loaded.IsActive)

        {

            _entries[userId] = new CacheEntry(loaded, nowUtc.Add(_ttl));

        }

        else

        {

            _entries.TryRemove(userId, out _);

        }



        return loaded;

    }



    public void Invalidate(string userId)

    {

        if (!string.IsNullOrWhiteSpace(userId))

        {

            _entries.TryRemove(userId, out _);

        }

    }



    public void ClearAll() => _entries.Clear();

}
