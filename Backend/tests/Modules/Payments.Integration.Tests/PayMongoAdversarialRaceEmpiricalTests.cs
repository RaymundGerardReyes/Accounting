using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rge.Accounting.Host.Composition;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.PaymentAttempts.PollAndReconcile;
using Rge.Payments.Application.Ports;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Workers;
using Rge.Tests.Common;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Empirical Challenger Suite for Milestone 4:
/// Adversarially stress-tests backend concurrent race conditions, double-capture resolution,
/// out-of-order webhook delivery, and terminal Succeeded state immutability.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PayMongoAdversarialRaceEmpiricalTests
{
    private const string WebhookSecret = "whsec_empirical_challenger_secret_2026";

    private sealed class AdversarialFakeGateway : IPaymentGateway
    {
        private readonly IClock _clock;
        private readonly ConcurrentDictionary<string, GatewayPaymentStatusResult> _sessions = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentBag<string> ExpiredCalls { get; } = [];
        public ConcurrentBag<string> StatusCalls { get; } = [];
        private int _failFirstStatusCheckWith400ForSource;
        public int FailFirstStatusCheckWith400ForSource
        {
            get => _failFirstStatusCheckWith400ForSource;
            set => _failFirstStatusCheckWith400ForSource = value;
        }

        public AdversarialFakeGateway(IClock clock)
        {
            _clock = clock;
        }

        public void SetRemoteSession(
            string providerPaymentId,
            string status,
            string? paymentMethodUsed = "qrph",
            DateTimeOffset? paidAtUtc = null,
            string? failureCode = null,
            string? failureMessage = null)
        {
            _sessions[providerPaymentId] = new GatewayPaymentStatusResult(
                ProviderPaymentId: providerPaymentId,
                Status: status,
                PaymentMethodUsed: paymentMethodUsed,
                PaidAtUtc: paidAtUtc,
                FailureCode: failureCode,
                FailureMessage: failureMessage);
        }

        public Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(
            CreateCheckoutSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            string csId = $"cs_adv_{Guid.NewGuid():N}"[..24];
            DateTimeOffset expiresAt = _clock.UtcNow.AddMinutes(30);

            _sessions[csId] = new GatewayPaymentStatusResult(
                ProviderPaymentId: csId,
                Status: "active",
                PaymentMethodUsed: null,
                PaidAtUtc: null,
                FailureCode: null,
                FailureMessage: null);

            return Task.FromResult(Result.Success(new CheckoutSessionResult(
                ProviderPaymentId: csId,
                CheckoutUrl: $"https://checkout.paymongo.com/{csId}",
                Status: "active",
                ExpiresAtUtc: expiresAt)));
        }

        public Task<Result<GatewayPaymentStatusResult>> GetCheckoutSessionStatusAsync(
            string providerPaymentId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls.Add(providerPaymentId);

            if (_failFirstStatusCheckWith400ForSource > 0)
            {
                Interlocked.Decrement(ref _failFirstStatusCheckWith400ForSource);
                return Task.FromResult(Result.Failure<GatewayPaymentStatusResult>(
                    Error.Validation("PayMongo.SourceAlreadyCaptured", "HTTP 400: Source is already processed and captured.")));
            }

            if (_sessions.TryGetValue(providerPaymentId, out GatewayPaymentStatusResult? status))
            {
                return Task.FromResult(Result.Success(status));
            }

            return Task.FromResult(Result.Failure<GatewayPaymentStatusResult>(
                Error.NotFound("PayMongo.SessionNotFound", $"Session '{providerPaymentId}' not found.")));
        }

        public Task<Result> ExpireCheckoutSessionAsync(
            string providerPaymentId,
            CancellationToken cancellationToken = default)
        {
            ExpiredCalls.Add(providerPaymentId);
            SetRemoteSession(providerPaymentId, "expired");
            return Task.FromResult(Result.Success());
        }

        public Task<Result<bool>> ProbeConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(true));
    }

    private sealed record PublishedNotification(string AttemptId, string Status, DateTimeOffset Timestamp);

    private sealed class SpyNotifier : IPaymentStatusNotifier
    {
        public ConcurrentQueue<PublishedNotification> Published { get; } = new();

        public Task NotifyPaymentStatusChangedAsync(
            string attemptId,
            string status,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Published.Enqueue(new PublishedNotification(attemptId, status, updatedAtUtc));
            return Task.CompletedTask;
        }
    }

    private static (ServiceProvider Sp, FakeClock Clock, AdversarialFakeGateway Gateway, SpyNotifier Notifier)
        BuildHarness()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 11, 14, 0, 0, TimeSpan.Zero));
        var gateway = new AdversarialFakeGateway(clock);
        var notifier = new SpyNotifier();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = WebhookSecret,
                ["Secrets:PayMongoSecretKey"] = "sk_test_adv_empirical_secret"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);

        services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
        services.Replace(ServiceDescriptor.Singleton<IPaymentGateway>(gateway));
        services.AddSingleton<IPaymentStatusNotifier>(notifier);
        services.AddScoped<PaymentExpirationWorker>();

        return (services.BuildServiceProvider(), clock, gateway, notifier);
    }

    // =========================================================================
    // TASK 1: Simultaneous capture race
    // A source.chargeable webhook and a reconciliation poll attempt capture at the same millisecond.
    // Does the attempt resolve to Succeeded without flipping to Failed or Expired?
    // =========================================================================

    [Fact]
    public async Task Task1_SimultaneousCaptureRace_SourceChargeableWebhookAndPoll_BothResolveToSucceededWithoutFlipping()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("PRODUCT-RACE-001", Quantity: 2));
        Assert.True(ordRes.IsSuccess);
        string orderId = ordRes.Value.Id;

        string srcId = $"src_race_{Guid.NewGuid():N}"[..24];
        var attempt = PaymentAttempt.CreatePending(
            orderId, "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), $"idem_race_{Guid.NewGuid():N}", clock.UtcNow);
        attempt.AttachCheckoutSession(srcId, $"https://checkout.paymongo.com/{srcId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        attempt.MarkProcessing(clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        // Gateway remote state has captured the payment to paid
        gateway.SetRemoteSession(srcId, "paid", "gcash", clock.UtcNow);

        // Prepare the source.chargeable webhook payload
        long nowUnix = clock.UtcNow.ToUnixTimeSeconds();
        string chargeablePayload = $$"""
        {
          "data": {
            "id": "evt_chargeable_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "source.chargeable",
              "data": {
                "id": "{{srcId}}",
                "attributes": {
                  "status": "chargeable",
                  "amount": {{ordRes.Value.AmountMinor}},
                  "currency": "PHP",
                  "source_type": "gcash"
                }
              }
            }
          }
        }
        """;
        string chargeableSig = WebhookSigner.CreateSignatureHeader(chargeablePayload, WebhookSecret, nowUnix);

        // Prepare payment.paid webhook payload as well
        string paidPayload = $$"""
        {
          "data": {
            "id": "evt_paid_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "{{srcId}}",
                "attributes": {
                  "status": "paid",
                  "amount": {{ordRes.Value.AmountMinor}},
                  "currency": "PHP",
                  "source": {
                    "id": "{{srcId}}"
                  }
                }
              }
            }
          }
        }
        """;
        string paidSig = WebhookSigner.CreateSignatureHeader(paidPayload, WebhookSecret, nowUnix);

        // Execute 10 simultaneous capture attempts (5 polls + 5 webhooks)
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pollTasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            await barrier.Task;
            var r = await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt.Id, AutoReconcile: true));
            return (Result)r;
        });

        var webhookTasks = Enumerable.Range(0, 3).Select(async i =>
        {
            await barrier.Task;
            var r = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(
                chargeableSig, chargeablePayload, $"sim-chargeable-{i}"));
            return (Result)r;
        });

        var paidWebhookTasks = Enumerable.Range(0, 2).Select(async i =>
        {
            await barrier.Task;
            var r = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(
                paidSig, paidPayload, $"sim-paid-{i}"));
            return (Result)r;
        });

        // Release all concurrent tasks at the exact same millisecond
        barrier.SetResult();

        var allResults = await Task.WhenAll(pollTasks.Concat(webhookTasks).Concat(paidWebhookTasks));

        // Verify that every single call succeeded without crashing or 500
        Assert.All(allResults, res => Assert.True(res.IsSuccess));

        // Verify Attempt final state
        PaymentAttempt? finalAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(finalAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, finalAttempt.Status);
        Assert.NotNull(finalAttempt.PaidAt);
        Assert.Null(finalAttempt.FailureCode);
        Assert.Null(finalAttempt.FailureMessage);

        // Verify Order final state
        Order? finalOrder = await orderRepo.GetByIdAsync(orderId);
        Assert.NotNull(finalOrder);
        Assert.Equal("paid", finalOrder.Status);
    }

    [Fact]
    public async Task Task1_DoubleCapture_HTTP400AlreadyCapturedRecovery_ResolvesToSucceededWithoutFlipping()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("PRODUCT-DBLCAP-002", Quantity: 1));
        string srcId = $"src_dbl_{Guid.NewGuid():N}"[..24];

        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-dbl-400", clock.UtcNow);
        attempt.AttachCheckoutSession(srcId, $"https://checkout.paymongo.com/{srcId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        attempt.MarkProcessing(clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        // Simulate PayMongo returning HTTP 400 (already processed/captured) on the first check,
        // followed by status check returning "paid"
        gateway.SetRemoteSession(srcId, "paid", "gcash", clock.UtcNow);
        gateway.FailFirstStatusCheckWith400ForSource = 1;

        // Poll query executes and recovers from HTTP 400
        var pollRes = await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt.Id, AutoReconcile: true));
        Assert.True(pollRes.IsSuccess);
        Assert.Equal("succeeded", pollRes.Value.Status.ToLowerInvariant());

        PaymentAttempt? reloaded = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(PaymentAttemptStatus.Succeeded, reloaded.Status);
        Assert.Null(reloaded.FailureCode);

        Order? reloadedOrder = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        Assert.NotNull(reloadedOrder);
        Assert.Equal("paid", reloadedOrder.Status);
    }

    // =========================================================================
    // TASK 2: Late/Delayed event race
    // Once an attempt reaches Succeeded, bombard it with delayed webhooks (payment.failed, qrph.expired)
    // and poll requests against expired gateway sessions. Does Succeeded state remain permanently locked?
    // =========================================================================

    [Fact]
    public async Task Task2_LateDelayedEventsBombardment_SucceededAttemptRemainsPermanentlyLocked()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var worker = scope.ServiceProvider.GetRequiredService<PaymentExpirationWorker>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("PRODUCT-BOMBARD-001", Quantity: 1));
        string csId = $"cs_bombard_{Guid.NewGuid():N}"[..24];

        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "qrph", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-bombard-001", clock.UtcNow);
        attempt.AttachCheckoutSession(csId, $"https://checkout.paymongo.com/{csId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);

        // 1. Settle to Succeeded
        DateTimeOffset settledPaidAt = clock.UtcNow;
        attempt.MarkSucceeded(settledPaidAt, "qrph");
        await attemptRepo.SaveAsync(attempt);

        Order? order = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        order!.MarkPaid(settledPaidAt);
        await orderRepo.SaveAsync(order);

        // 2. Configure gateway to report that the remote session is EXPIRED and FAILED
        gateway.SetRemoteSession(csId, "expired", failureCode: "source_expired", failureMessage: "Session expired on gateway");

        // 3. Prepare delayed payloads
        long unixNow = clock.UtcNow.ToUnixTimeSeconds();

        string failedPayload = $$"""
        {
          "data": {
            "id": "evt_late_failed_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "failed",
                  "failed_code": "card_declined",
                  "failed_message": "Card was declined on bank network."
                }
              }
            }
          }
        }
        """;
        string failedSig = WebhookSigner.CreateSignatureHeader(failedPayload, WebhookSecret, unixNow);

        string expiredPayload = $$"""
        {
          "data": {
            "id": "evt_late_expired_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "qrph.expired",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "expired"
                }
              }
            }
          }
        }
        """;
        string expiredSig = WebhookSigner.CreateSignatureHeader(expiredPayload, WebhookSecret, unixNow);

        // 4. Bombard the Succeeded attempt with 20 parallel delayed requests:
        // - 5 delayed payment.failed webhooks
        // - 5 delayed qrph.expired webhooks
        // - 8 poll requests against the expired gateway session
        // - 2 expiration worker sweep passes
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(async () =>
            {
                await barrier.Task;
                await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(failedSig, failedPayload, $"bombard-fail-{idx}"));
            }));
        }

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(async () =>
            {
                await barrier.Task;
                await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(expiredSig, expiredPayload, $"bombard-exp-{idx}"));
            }));
        }

        for (int i = 0; i < 8; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await barrier.Task;
                await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt.Id, AutoReconcile: true));
            }));
        }

        tasks.Add(Task.Run(async () =>
        {
            await barrier.Task;
            await worker.ProcessOverdueBatchOnceAsync();
        }));

        tasks.Add(Task.Run(async () =>
        {
            await barrier.Task;
            clock.Advance(TimeSpan.FromHours(1));
            await worker.ProcessOverdueBatchOnceAsync();
        }));

        barrier.SetResult();
        await Task.WhenAll(tasks);

        // 5. Assert that Succeeded state remains permanently and immutably locked!
        PaymentAttempt? finalAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(finalAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, finalAttempt.Status);
        Assert.Equal(settledPaidAt, finalAttempt.PaidAt);
        Assert.Null(finalAttempt.FailureCode);
        Assert.Null(finalAttempt.FailureMessage);

        Order? finalOrder = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        Assert.NotNull(finalOrder);
        Assert.Equal("paid", finalOrder.Status);

        // The remote gateway's expire API must NOT have been called for this succeeded attempt
        Assert.DoesNotContain(csId, gateway.ExpiredCalls);
    }

    // =========================================================================
    // TASK 3: Out-of-order webhook delivery
    // Deliver payment.failed or qrph.expired before chargeable or paid.
    // Verify that premature terminal failure locks the attempt, and that paid locks against subsequent failures.
    // =========================================================================

    [Fact]
    public async Task Task3_OutOfOrder_PaymentFailedDeliveredBeforeSourceChargeable_LocksFailedState()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("PRODUCT-OOO-FAIL-001", Quantity: 1));
        string srcId = $"src_ooo_fail_{Guid.NewGuid():N}"[..24];

        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-ooo-fail", clock.UtcNow);
        attempt.AttachCheckoutSession(srcId, $"https://checkout.paymongo.com/{srcId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        long unixNow = clock.UtcNow.ToUnixTimeSeconds();

        // 1. Deliver payment.failed FIRST (out-of-order)
        string failedPayload = $$"""
        {
          "data": {
            "id": "evt_ooo_failed_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "{{srcId}}",
                "attributes": {
                  "status": "failed",
                  "failed_code": "insufficient_funds",
                  "failed_message": "User has insufficient balance"
                }
              }
            }
          }
        }
        """;
        string failedSig = WebhookSigner.CreateSignatureHeader(failedPayload, WebhookSecret, unixNow);
        var failRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(failedSig, failedPayload, "ooo-fail-1"));
        Assert.True(failRes.IsSuccess);

        PaymentAttempt? failedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(failedAttempt);
        Assert.Equal(PaymentAttemptStatus.Failed, failedAttempt.Status);
        Assert.Equal("insufficient_funds", failedAttempt.FailureCode);

        // 2. Deliver source.chargeable AFTER payment.failed
        string chargeablePayload = $$"""
        {
          "data": {
            "id": "evt_ooo_charge_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "source.chargeable",
              "data": {
                "id": "{{srcId}}",
                "attributes": {
                  "status": "chargeable"
                }
              }
            }
          }
        }
        """;
        string chargeableSig = WebhookSigner.CreateSignatureHeader(chargeablePayload, WebhookSecret, unixNow);
        var chargeableRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(chargeableSig, chargeablePayload, "ooo-chargeable-2"));
        Assert.True(chargeableRes.IsSuccess);

        // State Machine Invariant: terminal Failed status MUST NOT be overwritten to Processing!
        PaymentAttempt? afterChargeable = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(afterChargeable);
        Assert.Equal(PaymentAttemptStatus.Failed, afterChargeable.Status);
        Assert.Equal("insufficient_funds", afterChargeable.FailureCode);

        // Parent order must remain pending so customer can retry
        Order? order = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        Assert.NotNull(order);
        Assert.Equal("pending", order.Status);
    }

    [Fact]
    public async Task Task3_OutOfOrder_QrphExpiredDeliveredBeforeSourceChargeable_LocksExpiredState()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("PRODUCT-OOO-EXP-001", Quantity: 1));
        string srcId = $"src_ooo_exp_{Guid.NewGuid():N}"[..24];

        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "qrph", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-ooo-exp", clock.UtcNow);
        attempt.AttachCheckoutSession(srcId, $"https://checkout.paymongo.com/{srcId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        long unixNow = clock.UtcNow.ToUnixTimeSeconds();

        // 1. Deliver qrph.expired FIRST (out-of-order)
        string expiredPayload = $$"""
        {
          "data": {
            "id": "evt_ooo_expired_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "qrph.expired",
              "data": {
                "id": "{{srcId}}",
                "attributes": {
                  "status": "expired"
                }
              }
            }
          }
        }
        """;
        string expiredSig = WebhookSigner.CreateSignatureHeader(expiredPayload, WebhookSecret, unixNow);
        var expRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(expiredSig, expiredPayload, "ooo-exp-1"));
        Assert.True(expRes.IsSuccess);

        PaymentAttempt? expiredAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(expiredAttempt);
        Assert.Equal(PaymentAttemptStatus.Expired, expiredAttempt.Status);

        // 2. Deliver source.chargeable AFTER qrph.expired
        string chargeablePayload = $$"""
        {
          "data": {
            "id": "evt_ooo_charge_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "source.chargeable",
              "data": {
                "id": "{{srcId}}",
                "attributes": {
                  "status": "chargeable"
                }
              }
            }
          }
        }
        """;
        string chargeableSig = WebhookSigner.CreateSignatureHeader(chargeablePayload, WebhookSecret, unixNow);
        var chargeableRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(chargeableSig, chargeablePayload, "ooo-chargeable-exp-2"));
        Assert.True(chargeableRes.IsSuccess);

        // State Machine Invariant: terminal Expired status MUST NOT be overwritten to Processing!
        PaymentAttempt? afterChargeable = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(afterChargeable);
        Assert.Equal(PaymentAttemptStatus.Expired, afterChargeable.Status);

        // Parent order must remain pending
        Order? order = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        Assert.NotNull(order);
        Assert.Equal("pending", order.Status);
    }
}
