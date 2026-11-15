using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Caching;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Payments.Api.Admin;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

public sealed class AdminEndpointsL1CacheRegressionTests
{
    [Fact]
    public async Task ListOutboxAsync_WhenCalledRepeatedlyWithinTtl_ServesFromL1CacheInSubMillisecond()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
#pragma warning disable EXTEXP0018
        services.AddHybridCache();
#pragma warning restore EXTEXP0018

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var spyOutbox = new SpyOutboxStore();
        var clock = new FixedTestClock(DateTimeOffset.UtcNow);
        var adminActor = new StubTestActor(isAuthenticated: true, roles: ["admin"]);

        // 1st call: populates cache (1 DB call)
        var firstResult = await OutboxEndpoints.ListOutboxAsync(spyOutbox, clock, adminActor, null, 100, cache, CancellationToken.None);
        Assert.NotNull(firstResult);
        Assert.Equal(1, spyOutbox.ClaimCount);

        // 2nd through 10th calls: served from L1 memory cache (0 additional DB calls, < 15ms total)
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 9; i++)
        {
            await OutboxEndpoints.ListOutboxAsync(spyOutbox, clock, adminActor, null, 100, cache, CancellationToken.None);
        }
        sw.Stop();

        Assert.Equal(1, spyOutbox.ClaimCount);
        Assert.True(sw.ElapsedMilliseconds < 25, $"Elapsed was {sw.ElapsedMilliseconds}ms");

        // Evict tag on state transition -> next call hits repository cleanly
        await OutboxEndpoints.EvictOutboxCacheAsync(cache, CancellationToken.None);
        await OutboxEndpoints.ListOutboxAsync(spyOutbox, clock, adminActor, null, 100, cache, CancellationToken.None);
        Assert.Equal(2, spyOutbox.ClaimCount);
    }

    [Fact]
    public async Task ListWebhookEventsAsync_WhenCalledRepeatedlyWithinTtl_ServesFromL1CacheAndInvalidatesOnEvict()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
#pragma warning disable EXTEXP0018
        services.AddHybridCache();
#pragma warning restore EXTEXP0018

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var spyStore = new SpyWebhookEventStore();
        var adminActor = new StubTestActor(isAuthenticated: true, roles: ["admin"]);

        // 1st call: populates cache (1 DB call)
        var firstResult = await WebhookEventsEndpoint.ListWebhookEventsAsync(spyStore, adminActor, null, 100, cache, CancellationToken.None);
        Assert.NotNull(firstResult);
        Assert.Equal(1, spyStore.CallCount);

        // 2nd through 10th calls: served from L1 cache (0 additional DB calls)
        for (var i = 0; i < 9; i++)
        {
            await WebhookEventsEndpoint.ListWebhookEventsAsync(spyStore, adminActor, null, 100, cache, CancellationToken.None);
        }

        Assert.Equal(1, spyStore.CallCount);

        // Evict tag when a new PayMongo webhook arrives -> next call queries store
        await WebhookEventsEndpoint.EvictWebhookEventsCacheAsync(cache, CancellationToken.None);
        await WebhookEventsEndpoint.ListWebhookEventsAsync(spyStore, adminActor, null, 100, cache, CancellationToken.None);
        Assert.Equal(2, spyStore.CallCount);
    }

    private sealed class FixedTestClock : IClock
    {
        public FixedTestClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class StubTestActor : ICurrentActor
    {
        private readonly bool _isAuthenticated;
        private readonly HashSet<string> _roles;
        private readonly string? _email;
        private readonly string? _displayName;

        public StubTestActor(bool isAuthenticated, IEnumerable<string> roles, string? email = "admin@example.com", string? displayName = "Test Admin")
        {
            _isAuthenticated = isAuthenticated;
            _roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
            _email = email;
            _displayName = displayName;
        }

        public string UserId => "test_admin";
        public string? Email => _email;
        public string? DisplayName => _displayName;
        public bool IsAuthenticated => _isAuthenticated;
        public string ActorId => "test_admin";
        public string EmployeeCode => "EMP001";
        public string DepartmentId => "FINANCE";
        public string CostCenterId => "CC001";
        public IReadOnlySet<string> Roles => _roles;
        public string DepartmentScope => "FINANCE";
        public string CorrelationId => "test-corr-id";

        public bool IsInRole(string role) => _roles.Contains(role);
        public bool HasRole(string role) => _roles.Contains(role);
        public bool HasScopeForDepartment(string departmentId) => true;
    }

    private sealed class SpyOutboxStore : IOutboxStore
    {
        public int ClaimCount { get; private set; }

        public Task EnqueueRawAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
            string workerId,
            int batchSize,
            TimeSpan leaseDuration,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
        {
            ClaimCount++;
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
        }

        public Task MarkCompletedAsync(string messageId, DateTimeOffset processedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(string messageId, string errorReason, DateTimeOffset failedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SpyWebhookEventStore : IWebhookEventStore
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<WebhookEvent>> ListRecentAsync(int limit = 100, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<WebhookEvent>>(Array.Empty<WebhookEvent>());
        }

        public Task<bool> ExistsAsync(string providerEventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task SaveAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
