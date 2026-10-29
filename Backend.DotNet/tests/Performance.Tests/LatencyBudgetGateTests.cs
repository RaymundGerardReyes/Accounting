using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.Accounting.Host.Composition;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.Orders.ListOrders;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Xunit;

using InitiatePaymentAttemptCommand = Rge.Payments.Application.PaymentAttempts.InitiateAttempt.InitiateAttemptCommand;
using InitiatePaymentAttemptHandler = Rge.Payments.Application.PaymentAttempts.InitiateAttempt.InitiateAttemptHandler;

namespace Rge.Performance.Tests;

public sealed class LatencyBudgetGateTests
{
    [Fact]
    public async Task P99_Latency_Budgets_Are_Satisfied_For_Webhooks_Orders_And_Reads()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = "whsec_perf_gate_secret"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);

        await using ServiceProvider sp = services.BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var listOrders = scope.ServiceProvider.GetRequiredService<ListOrdersHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiatePaymentAttemptHandler>();
        var webhookHandler = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();

        // Warmup JIT
        var warmOrder = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var warmAttempt = await initiateAttempt.HandleAsync(new InitiatePaymentAttemptCommand(warmOrder.Value.Id));
        _ = await listOrders.HandleAsync(new ListOrdersQuery(Limit: 50));

        const int iterations = 100;
        var createOrderLatencies = new double[iterations];
        var readOrderLatencies = new double[iterations];
        var webhookLatencies = new double[iterations];

        long unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int i = 0; i < iterations; i++)
        {
            // 1. Measure POST create order (< 80 ms p99 budget)
            long t0 = Stopwatch.GetTimestamp();
            var ord = await createOrder.HandleAsync(new CreateOrderCommand("CPA-REV-AUDIT-2026", Quantity: 1));
            createOrderLatencies[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

            var att = await initiateAttempt.HandleAsync(new InitiatePaymentAttemptCommand(ord.Value.Id));

            // 2. Measure GET list orders (< 30 ms p99 budget)
            long t1 = Stopwatch.GetTimestamp();
            _ = await listOrders.HandleAsync(new ListOrdersQuery(Limit: 50));
            readOrderLatencies[i] = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;

            // 3. Measure POST /webhooks/paymongo (< 50 ms p99 budget)
            string body = "{\"data\":{\"id\":\"evt_perf_" + i + "\",\"attributes\":{\"type\":\"checkout_session.payment.paid\",\"data\":{\"id\":\"" + att.Value.ProviderPaymentId + "\",\"attributes\":{\"payment_attempt_id\":\"" + att.Value.Id + "\"}}}}}";
            byte[] hmac = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes("whsec_perf_gate_secret"),
                Encoding.UTF8.GetBytes($"{unixNow}.{body}"));
            string sig = $"t={unixNow},te={Convert.ToHexStringLower(hmac)}";

            long t2 = Stopwatch.GetTimestamp();
            var whRes = await webhookHandler.HandleAsync(new ReceivePayMongoWebhookCommand(sig, body, $"corr-perf-{i}"));
            webhookLatencies[i] = Stopwatch.GetElapsedTime(t2).TotalMilliseconds;

            Assert.True(whRes.IsSuccess);
        }

        double createOrderP99 = ComputeP99(createOrderLatencies);
        double readOrdersP99 = ComputeP99(readOrderLatencies);
        double webhookP99 = ComputeP99(webhookLatencies);

        Assert.True(createOrderP99 < 80.0, $"CreateOrder p99 ({createOrderP99:F2} ms) exceeded 80 ms budget.");
        Assert.True(readOrdersP99 < 30.0, $"ListOrders read p99 ({readOrdersP99:F2} ms) exceeded 30 ms budget.");
        Assert.True(webhookP99 < 50.0, $"PayMongo Webhook p99 ({webhookP99:F2} ms) exceeded 50 ms budget.");
    }

    private static double ComputeP99(double[] samples)
    {
        Array.Sort(samples);
        int index = (int)Math.Ceiling(samples.Length * 0.99) - 1;
        return samples[Math.Clamp(index, 0, samples.Length - 1)];
    }
}
