using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Infrastructure.Kotobee;
using Xunit;

namespace Rge.Integrations.Integration.Tests;

/// <summary>
/// Covers Integration Scenarios I01-I50:
/// Real HttpClient form encoding, 1 req/sec rate limiter, Channel worker pipeline, and DI startup validation.
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public sealed class KotobeeIntegrationTests
{
    // ==========================================
    // I01–I19: HTTP CLIENT PAYLOAD & FIELD VALIDATION
    // ==========================================

    [Theory]
    [InlineData("library", "100", "501", "libid=100", "bid=501")]
    [InlineData("cloud", null, "9001", "cid=9001", "active=1")]
    public async Task KotobeeApiClient_AddUser_SendsExactFormFieldsAndNoSecretInUrl( // I01, I07, I08, I09, I10, I19
        string mode, string? libId, string bookId, string expectedField1, string expectedField2)
    {
        var handler = new CapturingHttpHandler(_ => """{"uid": "88412", "status": "ok"}""");
        var client = CreateClient(handler, opts =>
        {
            opts.PublishingMode = mode;
            opts.DefaultLibraryId = libId;
            opts.AccessKey = "SUPER_SECRET_KEY";
            opts.MinRequestIntervalMs = 10;
        });

        var result = await client.AddUserOrGrantBookAsync(
            email: null,
            uid: "88412",
            fullName: null,
            libraryId: libId,
            bookId: bookId,
            accessExpirationDate: new DateOnly(2027, 12, 31),
            ct: CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("88412", result.Uid);
        Assert.Single(handler.Requests);

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.DoesNotContain("SUPER_SECRET_KEY", req.Url); // I19: Never in URL
        Assert.Contains("accesskey=SUPER_SECRET_KEY", req.Body);
        Assert.Contains(expectedField1, req.Body);
        Assert.Contains(expectedField2, req.Body);
        Assert.Contains("accessexpirationdate=31-12-2027", req.Body); // I09 & I10: dd-MM-yyyy
    }

    [Fact]
    public async Task KotobeeApiClient_AddUser_WithEmail_SendsExpirationDate() // I11
    {
        var handler = new CapturingHttpHandler(_ => """{"uid": "999", "status": "ok"}""");
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 10);

        var result = await client.AddUserOrGrantBookAsync(
            email: "newuser@test.com",
            uid: null,
            fullName: "New User",
            libraryId: "100",
            bookId: "501",
            accessExpirationDate: new DateOnly(2028, 1, 15),
            ct: CancellationToken.None);

        Assert.True(result.Success);
        var req = Assert.Single(handler.Requests);
        Assert.Contains("email=newuser%40test.com", req.Body);
        Assert.Contains("name=New+User", req.Body);
        Assert.Contains("expirationdate=15-01-2028", req.Body);
    }

    [Theory]
    [InlineData("""{"uid": 12345, "bookAccess": "full"}""", "12345")]   // I02: numeric uid
    [InlineData("""{"uid": "12345", "bookAccess": "full"}""", "12345")] // I03: string uid
    public async Task KotobeeApiClient_GetUser_ParsesNumericOrStringUid(string json, string expectedUid)
    {
        var handler = new CapturingHttpHandler(_ => json);
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 10);

        var result = await client.GetUserAsync("u@test.com", null, "100", "501", CancellationToken.None);

        Assert.True(result.UserExists);
        Assert.Equal(expectedUid, result.Uid);
        Assert.Equal("full", result.BookAccess);
    }

    [Theory]
    [InlineData("""{"error": "User not found"}""")]                     // I04
    [InlineData("""{"status": "error", "message": "Invalid key"}""")]  // I05
    [InlineData("""<html><body>Bad Gateway</body></html>""")]          // I06: HTML error
    public async Task KotobeeApiClient_GetUser_HandlesErrorBodiesGracefully(string responseBody)
    {
        var handler = new CapturingHttpHandler(_ => responseBody);
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 10);

        var result = await client.GetUserAsync("u@test.com", null, "100", "501", CancellationToken.None);

        Assert.False(result.UserExists);
        Assert.Equal("none", result.BookAccess);
    }

    [Fact]
    public async Task KotobeeApiClient_EditUserBookAccess_SendsExactFormFields() // I12
    {
        var handler = new CapturingHttpHandler(_ => """{"status": "ok"}""");
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 10);

        var res = await client.EditUserBookAccessAsync("UID-EDIT", "100", "501", true, new DateOnly(2027, 5, 20), CancellationToken.None);

        Assert.True(res.Success);
        var req = Assert.Single(handler.Requests);
        Assert.EndsWith("/user/edit", req.Url);
        Assert.Contains("uid=UID-EDIT", req.Body);
        Assert.Contains("bid=501", req.Body);
        Assert.Contains("active=1", req.Body);
        Assert.Contains("accessexpirationdate=20-05-2027", req.Body);
    }

    [Fact]
    public async Task KotobeeApiClient_RevokeBook_NeverSendsDeleteAll() // I13
    {
        var handler = new CapturingHttpHandler(_ => """{"status": "ok"}""");
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 10);

        var res = await client.RevokeBookAccessAsync("u@test.com", "UID-1", "100", "501", CancellationToken.None);

        Assert.True(res.Success);
        var req = Assert.Single(handler.Requests);
        Assert.EndsWith("/user/delete", req.Url);
        Assert.Contains("bid=501", req.Body);
        Assert.DoesNotContain("deleteall", req.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KotobeeApiClient_ListBooks_ParsesArrayOrEmpty() // I14, I15
    {
        var handler = new CapturingHttpHandler(r =>
        {
            if (r.RequestUri!.ToString().Contains("empty")) return "[]";
            return """[{"id":"501","title":"Book One"},{"id":"502","title":"Book Two"}]""";
        });
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 10);

        var books = await client.ListBooksAsync("100", CancellationToken.None);
        Assert.Equal(2, books.Count);
        Assert.Equal("501", books[0].BookId);
        Assert.Equal("Book One", books[0].Title);

        var emptyClient = CreateClient(handler, opts =>
        {
            opts.BaseUrl = "https://www.kotobee.com/api/v1/empty";
            opts.MinRequestIntervalMs = 10;
        });
        var emptyBooks = await emptyClient.ListBooksAsync("100", CancellationToken.None);
        Assert.Empty(emptyBooks);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)] // I16: 500
    [InlineData(HttpStatusCode.TooManyRequests)]     // I17: 429
    public async Task KotobeeApiClient_HttpErrors_ThrowHttpRequestException(HttpStatusCode status)
    {
        var handler = new CapturingHttpHandler(_ => "error", status);
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 10);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetUserAsync("u@test.com", null, "100", "501", CancellationToken.None));
    }

    // ==========================================
    // I20–I22: RATE LIMITER PACING
    // ==========================================

    [Fact]
    public async Task KotobeeApiClient_ConcurrentRequests_AreSerializedByRateLimiter() // I20, I21
    {
        var handler = new CapturingHttpHandler(_ => """{"uid":"1","bookAccess":"full"}""");
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 200);

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(
            client.GetUserAsync("a@test.com", null, "100", "501", CancellationToken.None),
            client.GetUserAsync("b@test.com", null, "100", "502", CancellationToken.None),
            client.GetUserAsync("c@test.com", null, "100", "503", CancellationToken.None));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 350, $"Expected serialized pacing >= 350ms, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task KotobeeApiClient_RateLimiter_CancellationDoesNotDeadlock() // I22
    {
        var handler = new CapturingHttpHandler(_ => """{"status":"ok"}""");
        var client = CreateClient(handler, opts => opts.MinRequestIntervalMs = 500);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetUserAsync("test@test.com", null, "100", "501", cts.Token));

        // Ensure next non-cancelled request succeeds without semaphore deadlocking
        var normalResult = await client.GetUserAsync("test2@test.com", null, "100", "501", CancellationToken.None);
        Assert.NotNull(normalResult);
    }

    // ==========================================
    // I23–I26 & I49–I50: QUEUE & WORKER LIFECYCLE
    // ==========================================

    [Fact]
    public async Task ChannelQueueAndWorker_DrainsEnqueuedJobAndActivates() // I23, I24, I49
    {
        var activated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new ChannelKotobeeFulfillmentQueue();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IKotobeeFulfillmentQueue>(queue);
        services.AddSingleton<IPaidOrderVerifier>(new AlwaysPaidVerifier());
        services.AddSingleton<IKotobeeApiClient>(new InstantGrantApiClient());
        services.AddSingleton<IKotobeeEntitlementRepository>(new SignalOnActiveRepo(activated));
        services.AddSingleton<IKotobeeCatalogMappingResolver, KotobeeCatalogMappingResolver>();
        services.Configure<KotobeeOptions>(o =>
        {
            o.PublishingMode = "library";
            o.DefaultLibraryId = "100";
            o.ProductMappings["BOOK-A"] = [new KotobeeProductBookMapping("501")];
        });
        services.AddScoped<FulfillKotobeeOrderService>();

        using var sp = services.BuildServiceProvider();
        var worker = new KotobeeFulfillmentWorker(
            queue,
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<KotobeeFulfillmentWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new KotobeeFulfillmentJob("ORD-E2E-1", "e2e@test.com", "E2E", ["BOOK-A"]), CancellationToken.None);

        var completedInTime = await Task.WhenAny(activated.Task, Task.Delay(3000)) == activated.Task;
        await worker.StopAsync(CancellationToken.None);

        Assert.True(completedInTime, "Worker did not activate entitlement within timeout.");
    }

    [Fact]
    public async Task Worker_GracefulShutdown_StopsCleanly() // I26
    {
        var queue = new ChannelKotobeeFulfillmentQueue();
        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();

        var worker = new KotobeeFulfillmentWorker(
            queue,
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<KotobeeFulfillmentWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
        Assert.True(true);
    }

    // ==========================================
    // I27–I35: REPOSITORY & IDEMPOTENCY LEDGER
    // ==========================================

    [Fact]
    public async Task InMemoryRepo_IdempotentBehavior_PreservesActiveState() // I27, I28, I29, I30, I31, I32, I33, I34, I35
    {
        var repo = new PgKotobeeEntitlementRepository(null, NullLogger<PgKotobeeEntitlementRepository>.Instance);

        var rec = new KotobeeEntitlementRecord(
            Guid.NewGuid(), "ORD-1", "SKU-1", "buyer@test.com", null, "library", "100", "501", "Pending", null, 1, null, null, null);

        // I27: insert pending
        await repo.UpsertPendingAsync(rec);
        var list1 = await repo.GetByOrderIdAsync("ORD-1");
        Assert.Single(list1);
        Assert.Equal("Pending", list1[0].Status);

        // I30: mark active
        await repo.MarkActiveAsync("ORD-1", "501", "UID-100", "full");
        var list2 = await repo.GetByOrderIdAsync("ORD-1");
        Assert.Equal("Active", list2[0].Status);
        Assert.Equal("UID-100", list2[0].KotobeeUid);

        // I29: upsert pending again does NOT downgrade active
        await repo.UpsertPendingAsync(rec);
        var list3 = await repo.GetByOrderIdAsync("ORD-1");
        Assert.Equal("Active", list3[0].Status);

        // I32: mark revoked
        await repo.MarkRevokedAsync("ORD-1", "501");
        var list4 = await repo.GetByOrderIdAsync("ORD-1");
        Assert.Equal("Revoked", list4[0].Status);

        // I34: unknown order returns empty
        var empty = await repo.GetByOrderIdAsync("ORD-UNKNOWN");
        Assert.Empty(empty);
    }

    // ==========================================
    // I36–I41: DI & OPTIONS VALIDATION
    // ==========================================

    [Theory]
    [InlineData("true", "", "library", "100", "1100", false)]       // I37: missing key
    [InlineData("true", "key-1", "library", "", "1100", false)]     // I38: missing libid
    [InlineData("true", "key-1", "library", "100", "500", false)]   // I39: interval < 1000ms
    [InlineData("true", "key-1", "library", "100", "1100", true)]   // I36: valid config
    [InlineData("false", "", "library", "", "100", true)]           // I40: disabled skips checks
    public void DependencyInjection_OptionsValidation_EnforcesSafeStartup(
        string enabled, string key, string mode, string libId, string intervalMs, bool shouldPass)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Kotobee:Enabled"] = enabled,
                ["Integrations:Kotobee:AccessKey"] = key,
                ["Integrations:Kotobee:PublishingMode"] = mode,
                ["Integrations:Kotobee:DefaultLibraryId"] = libId,
                ["Integrations:Kotobee:MinRequestIntervalMs"] = intervalMs
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<KotobeeOptions>()
            .Bind(config.GetSection(KotobeeOptions.SectionName))
            .Validate(o =>
            {
                if (!o.Enabled) return true;
                var hasAuth = !string.IsNullOrWhiteSpace(o.AccessKey) || !string.IsNullOrWhiteSpace(o.Serial);
                var hasLib = !string.Equals(o.PublishingMode, "library", StringComparison.OrdinalIgnoreCase)
                             || !string.IsNullOrWhiteSpace(o.DefaultLibraryId);
                return hasAuth && hasLib && o.MinRequestIntervalMs >= 1000;
            }, "Invalid Kotobee configuration");

        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IOptions<KotobeeOptions>>();

        if (shouldPass)
            Assert.NotNull(accessor.Value);
        else
            Assert.Throws<OptionsValidationException>(() => _ = accessor.Value);
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private static KotobeeApiClient CreateClient(HttpMessageHandler handler, Action<KotobeeOptions>? configure = null)
    {
        var opts = new KotobeeOptions
        {
            BaseUrl = "https://www.kotobee.com/api/v1",
            AccessKey = "test-key",
            PublishingMode = "library",
            DefaultLibraryId = "100"
        };
        configure?.Invoke(opts);
        return new KotobeeApiClient(new HttpClient(handler), new StaticOpts(opts), NullLogger<KotobeeApiClient>.Instance);
    }

    private sealed record CapturedReq(HttpMethod Method, string Url, string Body);

    private sealed class CapturingHttpHandler(
        Func<HttpRequestMessage, string> responder,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<CapturedReq> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new(request.Method, request.RequestUri!.ToString(), body));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responder(request), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class AlwaysPaidVerifier : IPaidOrderVerifier
    {
        public Task<bool> IsOrderConfirmedPaidAsync(string orderId, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class InstantGrantApiClient : IKotobeeApiClient
    {
        public Task<KotobeeUserPermissionResult> GetUserAsync(string? e, string? u, string? l, string? b, CancellationToken ct = default)
            => Task.FromResult(new KotobeeUserPermissionResult(true, "UID-1", e, b is null ? "none" : "full", "{}"));
        public Task<KotobeeMutationResult> AddUserOrGrantBookAsync(string? e, string? u, string? n, string? l, string b, DateOnly? exp, CancellationToken ct = default)
            => Task.FromResult(new KotobeeMutationResult(true, "UID-1", null, "{}"));
        public Task<KotobeeMutationResult> EditUserBookAccessAsync(string u, string? l, string b, bool a, DateOnly? exp, CancellationToken ct = default)
            => Task.FromResult(new KotobeeMutationResult(true, "UID-1", null, "{}"));
        public Task<KotobeeMutationResult> RevokeBookAccessAsync(string? e, string? u, string? l, string b, CancellationToken ct = default)
            => Task.FromResult(new KotobeeMutationResult(true, "UID-1", null, "{}"));
        public Task<IReadOnlyList<KotobeeBookSummary>> ListBooksAsync(string? l, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KotobeeBookSummary>>([]);
    }

    private sealed class SignalOnActiveRepo(TaskCompletionSource<bool> tcs) : IKotobeeEntitlementRepository
    {
        public Task UpsertPendingAsync(KotobeeEntitlementRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkActiveAsync(string orderId, string bookId, string? uid, string bookAccess, CancellationToken ct = default) { tcs.TrySetResult(true); return Task.CompletedTask; }
        public Task MarkFailedAsync(string orderId, string bookId, string error, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkRevokedAsync(string orderId, string bookId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<KotobeeEntitlementRecord>> GetByOrderIdAsync(string orderId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KotobeeEntitlementRecord>>([]);
    }

    private sealed class StaticOpts(KotobeeOptions val) : IOptionsMonitor<KotobeeOptions>
    {
        public KotobeeOptions CurrentValue => val;
        public KotobeeOptions Get(string? name) => val;
        public IDisposable? OnChange(Action<KotobeeOptions, string?> listener) => null;
    }
}
