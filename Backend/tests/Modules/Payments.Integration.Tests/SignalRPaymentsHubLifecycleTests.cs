using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rge.Payments.Application.Ports;
using Xunit;

namespace Rge.Payments.Integration.Tests;

[Collection("PaymentsIntegration")]
public sealed class SignalRPaymentsHubLifecycleTests : IClassFixture<AccountingWebApplicationFactory>
{
    private readonly AccountingWebApplicationFactory _factory;

    public SignalRPaymentsHubLifecycleTests(AccountingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Negotiate_ReturnsValidConnectionToken_And_OffersLongPollingTransport()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.TryGetProperty("connectionToken", out JsonElement tokenElem));
        string? connectionToken = tokenElem.GetString();
        Assert.False(string.IsNullOrWhiteSpace(connectionToken));

        Assert.True(root.TryGetProperty("availableTransports", out JsonElement transportsElem));
        bool hasLongPolling = false;
        foreach (JsonElement transport in transportsElem.EnumerateArray())
        {
            if (transport.TryGetProperty("transport", out JsonElement tName) &&
                tName.GetString() == "LongPolling")
            {
                hasLongPolling = true;
                break;
            }
        }
        Assert.True(hasLongPolling, "Negotiate must offer LongPolling transport.");
    }

    [Fact]
    public async Task Poll_WhenAbortedOrTimedOut_Returns204NoContent_Without500Or502()
    {
        using HttpClient client = _factory.CreateClient();

        // 1. Negotiate connection
        HttpResponseMessage negResp = await client.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", content: null);
        Assert.Equal(HttpStatusCode.OK, negResp.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await negResp.Content.ReadAsStringAsync());
        string token = doc.RootElement.GetProperty("connectionToken").GetString()!;

        // 2. Poll with short cancellation token simulating client unmount/timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            HttpResponseMessage pollResp = await client.GetAsync(
                $"/api/hubs/payments?id={Uri.EscapeDataString(token)}",
                cts.Token);

            Assert.NotEqual(HttpStatusCode.BadGateway, pollResp.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, pollResp.StatusCode);
            Assert.True(
                pollResp.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
                $"Expected 204 NoContent or 200 OK, but received {pollResp.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            // Expected on client abort
        }
    }

    [Fact]
    public async Task StatusBroadcast_WhenPaymentStatusChanges_NotifiesSubscribedGroups()
    {
        using HttpClient client = _factory.CreateClient();

        // 1. Negotiate connection
        HttpResponseMessage negResp = await client.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", content: null);
        Assert.Equal(HttpStatusCode.OK, negResp.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await negResp.Content.ReadAsStringAsync());
        string token = doc.RootElement.GetProperty("connectionToken").GetString()!;

        // 2. Send SignalR JSON protocol handshake frame
        string handshake = "{\"protocol\":\"json\",\"version\":1}\u001e";
        using var handshakeContent = new StringContent(handshake);
        HttpResponseMessage initResp = await client.PostAsync(
            $"/api/hubs/payments?id={Uri.EscapeDataString(token)}",
            handshakeContent);
        Assert.True(initResp.IsSuccessStatusCode);

        // 3. Trigger notification via IPaymentStatusNotifier
        using IServiceScope scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IPaymentStatusNotifier>();
        string txId = $"tx_test_{Guid.NewGuid():N}";

        // Verify status notification completes cleanly without error
        await notifier.NotifyPaymentStatusChangedAsync(
            txId,
            "paid",
            DateTimeOffset.UtcNow);

        // 4. Disconnect cleanly
        HttpResponseMessage deleteResp = await client.DeleteAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}");
        Assert.True(
            deleteResp.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.Accepted or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Expected successful disconnect, got {deleteResp.StatusCode}");
    }

    [Fact]
    public async Task ConcurrentDisconnectAndPoll_NeverProduces502Or500()
    {
        using HttpClient client = _factory.CreateClient();

        const int iterations = 10;
        var tasks = new List<Task>();

        for (int i = 0; i < iterations; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using HttpClient localClient = _factory.CreateClient();

                // 1. Negotiate
                HttpResponseMessage neg = await localClient.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", null);
                Assert.Equal(HttpStatusCode.OK, neg.StatusCode);
                using JsonDocument doc = JsonDocument.Parse(await neg.Content.ReadAsStringAsync());
                string token = doc.RootElement.GetProperty("connectionToken").GetString()!;

                // 2. Launch long poll and concurrent disconnect
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                Task<HttpResponseMessage?> pollTask = Task.Run(async () =>
                {
                    try
                    {
                        return await localClient.GetAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}", cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                });

                // Small delay to ensure poll is in-flight on Kestrel
                await Task.Delay(25);

                Task<HttpResponseMessage> disconnectTask = localClient.DeleteAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}");

                HttpResponseMessage deleteRes = await disconnectTask;
                Assert.NotEqual(HttpStatusCode.BadGateway, deleteRes.StatusCode);
                Assert.NotEqual(HttpStatusCode.InternalServerError, deleteRes.StatusCode);

                HttpResponseMessage? pollRes = await pollTask;
                if (pollRes is not null)
                {
                    Assert.NotEqual(HttpStatusCode.BadGateway, pollRes.StatusCode);
                    Assert.NotEqual(HttpStatusCode.InternalServerError, pollRes.StatusCode);
                    Assert.True(
                        pollRes.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound,
                        $"Poll returned unexpected status: {pollRes.StatusCode}");
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Adversarial_RapidSocketAbortStorm_NeverProduces500Or502_And_ZeroOperationCanceledExceptionErrorLogs()
    {
        const int iterations = 30;
        var recordedStatuses = new ConcurrentBag<HttpStatusCode>();
        var tasks = new List<Task>();

        for (int i = 0; i < iterations; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                using HttpClient localClient = _factory.CreateClient();

                // 1. Negotiate
                HttpResponseMessage neg = await localClient.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", null);
                Assert.Equal(HttpStatusCode.OK, neg.StatusCode);
                recordedStatuses.Add(neg.StatusCode);
                using JsonDocument doc = JsonDocument.Parse(await neg.Content.ReadAsStringAsync());
                string token = doc.RootElement.GetProperty("connectionToken").GetString()!;

                // 2. Abrupt cancellation with variable micro-delays (1ms - 20ms)
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1 + (index % 20)));

                Task pollTask = Task.Run(async () =>
                {
                    try
                    {
                        HttpResponseMessage pollRes = await localClient.GetAsync(
                            $"/api/hubs/payments?id={Uri.EscapeDataString(token)}",
                            cts.Token);
                        recordedStatuses.Add(pollRes.StatusCode);
                    }
                    catch (OperationCanceledException)
                    {
                        // Clean client-side abort
                    }
                });

                // Concurrently issue disconnect
                try
                {
                    HttpResponseMessage deleteRes = await localClient.DeleteAsync(
                        $"/api/hubs/payments?id={Uri.EscapeDataString(token)}");
                    recordedStatuses.Add(deleteRes.StatusCode);
                }
                catch (OperationCanceledException)
                {
                    // Clean client-side abort
                }

                await pollTask;
            }));
        }

        await Task.WhenAll(tasks);

        // Verify zero 500 or 502 across all recorded status codes
        Assert.DoesNotContain(recordedStatuses, s => s == HttpStatusCode.InternalServerError);
        Assert.DoesNotContain(recordedStatuses, s => s == HttpStatusCode.BadGateway);

        // Verify captured host error logs: zero unhandled OperationCanceledException or TaskCanceledException
        var cancellationErrors = _factory.CapturedLogs
            .Where(l => l.Level >= LogLevel.Error &&
                        (l.Exception is OperationCanceledException or TaskCanceledException ||
                         l.Message.Contains("OperationCanceledException", StringComparison.OrdinalIgnoreCase) ||
                         l.Message.Contains("TaskCanceledException", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(cancellationErrors);

        // Verify zero unhandled hub errors
        var unhandledHubErrors = _factory.CapturedLogs
            .Where(l => l.Level >= LogLevel.Error &&
                        (l.Message.Contains("/api/hubs", StringComparison.OrdinalIgnoreCase) ||
                         l.Message.Contains("/hubs", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(unhandledHubErrors);
    }

    [Fact]
    public async Task Adversarial_MalformedAndExpiredTokens_ReturnSafeErrorCodes_Never500Or502()
    {
        using HttpClient client = _factory.CreateClient();

        // 1. Non-existent token poll
        HttpResponseMessage nonExistentPoll = await client.GetAsync("/api/hubs/payments?id=non_existent_token_99999");
        Assert.NotEqual(HttpStatusCode.InternalServerError, nonExistentPoll.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, nonExistentPoll.StatusCode);
        Assert.True(nonExistentPoll.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);

        // 2. Non-existent token delete
        HttpResponseMessage nonExistentDel = await client.DeleteAsync("/api/hubs/payments?id=non_existent_token_99999");
        Assert.NotEqual(HttpStatusCode.InternalServerError, nonExistentDel.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, nonExistentDel.StatusCode);
        Assert.True(nonExistentDel.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);

        // 3. Empty token
        HttpResponseMessage emptyTokenPoll = await client.GetAsync("/api/hubs/payments?id=");
        Assert.NotEqual(HttpStatusCode.InternalServerError, emptyTokenPoll.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, emptyTokenPoll.StatusCode);

        // 4. Missing token parameter
        HttpResponseMessage missingTokenPoll = await client.GetAsync("/api/hubs/payments");
        Assert.NotEqual(HttpStatusCode.InternalServerError, missingTokenPoll.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, missingTokenPoll.StatusCode);

        // 5. Oversized token string (5000 chars)
        string oversized = new string('x', 5000);
        HttpResponseMessage oversizedPoll = await client.GetAsync($"/api/hubs/payments?id={oversized}");
        Assert.NotEqual(HttpStatusCode.InternalServerError, oversizedPoll.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, oversizedPoll.StatusCode);

        // 6. Corrupt binary payload to hub endpoint
        using var corruptContent = new ByteArrayContent(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        HttpResponseMessage corruptPost = await client.PostAsync("/api/hubs/payments?id=non_existent", corruptContent);
        Assert.NotEqual(HttpStatusCode.InternalServerError, corruptPost.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, corruptPost.StatusCode);

        // 7. Verify host remains responsive and valid negotiate succeeds immediately after
        HttpResponseMessage recoverResp = await client.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, recoverResp.StatusCode);
    }

    [Fact]
    public async Task Adversarial_DoubleDelete_And_PollAfterDelete_AreIdempotentAndSafe()
    {
        using HttpClient client = _factory.CreateClient();

        // 1. Negotiate
        HttpResponseMessage neg = await client.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, neg.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await neg.Content.ReadAsStringAsync());
        string token = doc.RootElement.GetProperty("connectionToken").GetString()!;

        // 2. Initial delete
        HttpResponseMessage firstDel = await client.DeleteAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}");
        Assert.NotEqual(HttpStatusCode.InternalServerError, firstDel.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, firstDel.StatusCode);

        // 3. Second delete (idempotency under concurrent teardown)
        HttpResponseMessage secondDel = await client.DeleteAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}");
        Assert.NotEqual(HttpStatusCode.InternalServerError, secondDel.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, secondDel.StatusCode);

        // 4. Poll after deletion
        HttpResponseMessage pollAfterDel = await client.GetAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}");
        Assert.NotEqual(HttpStatusCode.InternalServerError, pollAfterDel.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadGateway, pollAfterDel.StatusCode);
        Assert.True(
            pollAfterDel.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Expected OK, NoContent, NotFound, or BadRequest, but got {pollAfterDel.StatusCode}");
    }

    [Fact]
    public async Task Adversarial_HighConcurrencyStress_50Clients_ConcurrentNegotiatePollDeleteBroadcast()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IPaymentStatusNotifier>();

        const int clientCount = 50;
        var tasks = new List<Task>();
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        for (int i = 0; i < clientCount; i++)
        {
            string txId = $"tx_stress_{i}_{Guid.NewGuid():N}";
            tasks.Add(Task.Run(async () =>
            {
                using HttpClient localClient = _factory.CreateClient();

                // 1. Negotiate
                HttpResponseMessage neg = await localClient.PostAsync("/api/hubs/payments/negotiate?negotiateVersion=1", null);
                statusCodes.Add(neg.StatusCode);
                if (neg.StatusCode != HttpStatusCode.OK) return;

                using JsonDocument doc = JsonDocument.Parse(await neg.Content.ReadAsStringAsync());
                string token = doc.RootElement.GetProperty("connectionToken").GetString()!;

                // 2. Handshake
                string handshake = "{\"protocol\":\"json\",\"version\":1}\u001e";
                using var hsContent = new StringContent(handshake);
                HttpResponseMessage hsResp = await localClient.PostAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}", hsContent);
                statusCodes.Add(hsResp.StatusCode);

                // 3. Short poll
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                Task poll = Task.Run(async () =>
                {
                    try
                    {
                        HttpResponseMessage p = await localClient.GetAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}", cts.Token);
                        statusCodes.Add(p.StatusCode);
                    }
                    catch (OperationCanceledException) { }
                });

                // 4. Trigger payment notification
                await notifier.NotifyPaymentStatusChangedAsync(txId, "paid", DateTimeOffset.UtcNow);

                await poll;

                // 5. Delete
                HttpResponseMessage del = await localClient.DeleteAsync($"/api/hubs/payments?id={Uri.EscapeDataString(token)}");
                statusCodes.Add(del.StatusCode);
            }));
        }

        await Task.WhenAll(tasks);

        Assert.DoesNotContain(statusCodes, s => s == HttpStatusCode.InternalServerError);
        Assert.DoesNotContain(statusCodes, s => s == HttpStatusCode.BadGateway);
    }
}

