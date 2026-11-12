using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rge.Accounting.Host.Composition;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.PaymentAttempts.PollAndReconcile;
using Rge.Payments.Application.Ports;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Workers;
using Rge.Receipts.Contracts;
using Rge.Tests.Common;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Proves the end-to-end PayMongo Checkout Session lifecycle, server-side expiration,
/// missed-webhook reconciliation, state-machine immutability, and SignalR notifications.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PayMongoLifecycleVerificationTests
{
    private const string WebhookSecret = "whsec_lifecycle_proof_secret_2026";

    // =========================================================================
    // Test Doubles: Controllable PayMongo Gateway & SignalR Spy Notifier
    // =========================================================================

    private sealed class FakePayMongoGateway : IPaymentGateway
    {
        private readonly IClock _clock;
        private readonly ConcurrentDictionary<string, GatewayPaymentStatusResult> _sessions = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentBag<string> ExpiredSessionCalls { get; } = [];
        public ConcurrentBag<string> StatusCalls { get; } = [];
        public bool RejectNextExpireCallWith400 { get; set; }

        public FakePayMongoGateway(IClock clock)
        {
            _clock = clock;
        }

        public void SetRemoteSessionStatus(
            string checkoutSessionId,
            string status,
            string? paymentMethodUsed = "qrph",
            DateTimeOffset? paidAtUtc = null,
            string? failureCode = null,
            string? failureMessage = null)
        {
            _sessions[checkoutSessionId] = new GatewayPaymentStatusResult(
                ProviderPaymentId: checkoutSessionId,
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
            string csId = $"cs_test_{Guid.NewGuid():N}"[..24];
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
            if (RejectNextExpireCallWith400)
            {
                return Task.FromResult(Result.Failure(
                    Error.Validation("PayMongo.ExpireFailed", "HTTP 400: Session cannot be expired.")));
            }

            ExpiredSessionCalls.Add(providerPaymentId);
            SetRemoteSessionStatus(providerPaymentId, "expired");
            return Task.FromResult(Result.Success());
        }

        public Task<Result<bool>> ProbeConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(true));
    }

    private sealed record NotificationEvent(string AttemptId, string Status, DateTimeOffset UpdatedAtUtc);

    private sealed class SpyPaymentStatusNotifier : IPaymentStatusNotifier
    {
        public ConcurrentQueue<NotificationEvent> PublishedEvents { get; } = new();

        public Task NotifyPaymentStatusChangedAsync(
            string attemptId,
            string status,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            PublishedEvents.Enqueue(new NotificationEvent(attemptId, status, updatedAtUtc));
            return Task.CompletedTask;
        }
    }

    private static (ServiceProvider Sp, FakeClock Clock, FakePayMongoGateway Gateway, SpyPaymentStatusNotifier Notifier)
        BuildTestHarness()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));
        var gateway = new FakePayMongoGateway(clock);
        var notifier = new SpyPaymentStatusNotifier();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = WebhookSecret,
                ["Secrets:PayMongoSecretKey"] = "sk_test_simulated_live_key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);

        // Replace clock, gateway, and SignalR notifier with deterministic test doubles
        services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
        services.Replace(ServiceDescriptor.Singleton<IPaymentGateway>(gateway));
        services.AddSingleton<IPaymentStatusNotifier>(notifier);
        services.AddScoped<PaymentExpirationWorker>();

        return (services.BuildServiceProvider(), clock, gateway, notifier);
    }

    // =========================================================================
    // Scenario 1: Abandoned Session Reaches Deadline -> Expire API + SignalR
    // =========================================================================
    [Fact]
    public async Task Scenario01_OverdueEligibleSession_CallsPayMongoExpire_UpdatesDb_And_PushesSignalR()
    {
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var worker = scope.ServiceProvider.GetRequiredService<PaymentExpirationWorker>();

        // 1. Create attempt with a 30-minute deadline and cs_ identifier
        var attempt = PaymentAttempt.CreatePending(
            "ord-exp-001", "qrph", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(1500_00), "idem-exp-001", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_eligible_001", "https://checkout.paymongo.com/cs_eligible_001", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);
        gateway.SetRemoteSessionStatus("cs_eligible_001", "active");

        // 2. At T+25 minutes (4:02 remaining), worker must NOT expire the session
        clock.Advance(TimeSpan.FromMinutes(25));
        await worker.ProcessOverdueBatchOnceAsync();

        PaymentAttempt? stillActive = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, stillActive!.Status);
        Assert.Empty(gateway.ExpiredSessionCalls);

        // 3. Advance past 30-minute deadline (T+31 minutes)
        clock.Advance(TimeSpan.FromMinutes(6));
        int processed = await worker.ProcessOverdueBatchOnceAsync();

        Assert.Equal(1, processed);
        Assert.Contains("cs_eligible_001", gateway.ExpiredSessionCalls);

        PaymentAttempt? expiredAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Expired, expiredAttempt!.Status);

        // 4. Verify SignalR notification was broadcast so Next.js stops its timer immediately
        Assert.True(notifier.PublishedEvents.TryDequeue(out NotificationEvent? signalrEvt));
        Assert.Equal(attempt.Id, signalrEvt.AttemptId);
        Assert.Equal(nameof(PaymentAttemptStatus.Expired), signalrEvt.Status);
    }

    // =========================================================================
    // Scenario 2: Missed Webhook Recovery -> Preserves Paid Status (Never Expires)
    // =========================================================================
    [Fact]
    public async Task Scenario02_MissedWebhook_AlreadyPaidOnPayMongo_PreservesPaid_IssuesReceipt_And_NeverExpires()
    {
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var worker = scope.ServiceProvider.GetRequiredService<PaymentExpirationWorker>();
        var outboxWorker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();
        var receiptLookup = scope.ServiceProvider.GetRequiredService<IReceiptLookup>();

        // 1. Create Order & Checkout Session
        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "qrph", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-missed-wh", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_paid_missed_wh", "https://checkout.paymongo.com/cs_paid_missed_wh", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        // 2. Customer pays on PayMongo, but the webhook is lost in transit!
        gateway.SetRemoteSessionStatus("cs_paid_missed_wh", "paid", "qrph", clock.UtcNow.AddMinutes(10));

        // 3. Deadline passes (T+31 minutes) and PaymentExpirationWorker runs
        clock.Advance(TimeSpan.FromMinutes(31));
        await worker.ProcessOverdueBatchOnceAsync();

        // 4. Prove Expire API was NEVER called and status reconciled to Succeeded
        Assert.DoesNotContain("cs_paid_missed_wh", gateway.ExpiredSessionCalls);

        PaymentAttempt? reconciledAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.Equal(PaymentAttemptStatus.Succeeded, reconciledAttempt!.Status);

        Order? reconciledOrder = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        Assert.Equal("paid", reconciledOrder!.Status);

        // 5. Outbox processes the reconciled payment and issues the BIR Official Receipt
        await outboxWorker.ProcessNextBatchOnceAsync();
        ReceiptSummaryContract? receipt = await receiptLookup.FindByOrderIdAsync(ordRes.Value.Id);
        Assert.NotNull(receipt);
    }

    // =========================================================================
    // Scenario 3: In-Flight Processing Payment -> Expiration Deferred
    // =========================================================================
    [Fact]
    public async Task Scenario03_ProcessingPaymentAtDeadline_DefersExpiration_And_KeepsSessionActive()
    {
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var worker = scope.ServiceProvider.GetRequiredService<PaymentExpirationWorker>();

        var attempt = PaymentAttempt.CreatePending(
            "ord-proc-001", "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(2000_00), "idem-proc-001", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_processing_001", "https://checkout.paymongo.com/cs_processing_001", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        // PayMongo reports customer is mid-authentication ("processing")
        gateway.SetRemoteSessionStatus("cs_processing_001", "processing");

        clock.Advance(TimeSpan.FromMinutes(31));
        await worker.ProcessOverdueBatchOnceAsync();

        // Must NOT call Expire API or transition to Expired
        Assert.Empty(gateway.ExpiredSessionCalls);
        PaymentAttempt? deferred = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, deferred!.Status);
        Assert.Empty(notifier.PublishedEvents);
    }

    // =========================================================================
    // Scenario 4: State Machine Immutability -> Expired Payment Cannot Be Revived
    // =========================================================================
    [Fact]
    public async Task Scenario04_LateWebhookAfterExpiration_CannotReviveExpiredPayment()
    {
        var (sp, clock, _, _) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var attempt = PaymentAttempt.CreatePending(
            "ord-revive-001", "qrph", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(1000_00), "idem-revive-001", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_expired_001", "https://checkout.paymongo.com/cs_expired_001", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        attempt.MarkExpired(clock.UtcNow.AddMinutes(31));
        await attemptRepo.SaveAsync(attempt);

        // Attempt to mark an already-expired payment as Succeeded must fail domain invariant
        Result lateSuccess = attempt.MarkSucceeded(clock.UtcNow.AddMinutes(35), "qrph");
        Assert.True(lateSuccess.IsFailure);
        Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);
    }

    // =========================================================================
    // Scenario 5: Duplicate Webhook Idempotency & Signature Verification
    // =========================================================================
    [Fact]
    public async Task Scenario05_DuplicatePaidWebhook_ProcessesOnce_And_PreventsDuplicateFulfillment()
    {
        var (sp, clock, _, _) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var outboxWorker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();

        // Drain any un-processed messages from earlier test suites running in the same process
        while (await outboxWorker.ProcessNextBatchOnceAsync() > 0)
        {
        }

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "qrph", "idem-dup-wh"));

        long unixNow = clock.UtcNow.ToUnixTimeSeconds();
        string rawPayload = $$"""
            {
              "data": {
                "id": "evt_dup_test_001",
                "attributes": {
                  "type": "checkout_session.payment.paid",
                  "data": {
                    "id": "{{attRes.Value.ProviderPaymentId}}",
                    "attributes": {
                      "payment_attempt_id": "{{attRes.Value.Id}}",
                      "payment_method_used": "qrph"
                    }
                  }
                }
              }
            }
            """;

        string sigHeader = WebhookSigner.CreateSignatureHeader(rawPayload, WebhookSecret, unixNow);

        // First delivery -> processed
        var firstDelivery = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "corr-1"));
        Assert.True(firstDelivery.IsSuccess);

        // Second delivery of the same evt_dup_test_001 -> idempotent duplicate ignored
        var secondDelivery = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "corr-2"));
        Assert.True(secondDelivery.IsSuccess);

        // Only 1 outbox message should have been enqueued
        int outboxCount = await outboxWorker.ProcessNextBatchOnceAsync();
        Assert.Equal(1, outboxCount);
    }

    // =========================================================================
    // Scenario 6: Fast-Path Reconciliation Cooldown & SignalR Event Notification
    // =========================================================================
    [Fact]
    public async Task Scenario06_PollAndReconcile_EnforcesCooldown_And_NotifiesSignalR()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "qrph", "idem-poll-cd"));
        string csId = attRes.Value.ProviderPaymentId!;

        // 1. Initial fast-path query with auto_reconcile = false: MUST NOT call gateway
        var localResult = await pollHandler.HandleAsync(new PollAndReconcileQuery(attRes.Value.Id, AutoReconcile: false));
        Assert.True(localResult.IsSuccess);
        Assert.Equal("requiresaction", localResult.Value.Status.ToLowerInvariant());
        Assert.Empty(gateway.StatusCalls);

        // 2. First query with auto_reconcile = true: Calls gateway, transitions to succeeded, publishes SignalR event
        gateway.SetRemoteSessionStatus(csId, "paid", "qrph", clock.UtcNow);
        var reconciledResult = await pollHandler.HandleAsync(new PollAndReconcileQuery(attRes.Value.Id, AutoReconcile: true));
        Assert.True(reconciledResult.IsSuccess);
        Assert.Equal("succeeded", reconciledResult.Value.Status);
        Assert.Single(gateway.StatusCalls);
        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == attRes.Value.Id && e.Status == "succeeded");

        // 3. Immediate repeat query within 3-second cooldown window: MUST NOT call gateway again
        var cachedResult = await pollHandler.HandleAsync(new PollAndReconcileQuery(attRes.Value.Id, AutoReconcile: true));
        Assert.True(cachedResult.IsSuccess);
        Assert.Equal("succeeded", cachedResult.Value.Status);
        Assert.Single(gateway.StatusCalls); // Still 1, not 2! Cooldown prevented redundant call
    }

    // =========================================================================
    // Scenario 7: Multi-Key Correlation & Session Invalidation on payment.failed
    // =========================================================================
    [Fact]
    public async Task Scenario07_PaymentFailedWebhook_WithPayResourceId_CorrelatesViaMetadata_LocksSession_And_NotifiesSignalR()
    {
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "card", "idem-fail-test"));
        string csId = attRes.Value.ProviderPaymentId!;
        string attemptId = attRes.Value.Id;

        // Construct realistic PayMongo payment.failed webhook where resource ID is pay_..., NOT cs_...
        long unixNow = clock.UtcNow.ToUnixTimeSeconds();
        string rawPayload = $$"""
            {
              "data": {
                "id": "evt_fail_proof_001",
                "type": "event",
                "attributes": {
                  "type": "payment.failed",
                  "data": {
                    "id": "pay_failed_declined_999",
                    "type": "payment",
                    "attributes": {
                      "status": "failed",
                      "failed_code": "card_declined",
                      "failed_message": "The customer card was declined due to insufficient funds.",
                      "metadata": {
                        "payment_attempt_id": "{{attemptId}}",
                        "order_id": "{{ordRes.Value.Id}}",
                        "idempotency_key": "idem-fail-test"
                      }
                    }
                  }
                }
              }
            }
            """;

        string sigHeader = WebhookSigner.CreateSignatureHeader(rawPayload, WebhookSecret, unixNow);
        var webhookRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "corr-fail-1"));

        Assert.True(webhookRes.IsSuccess);
        Assert.Equal("processed", webhookRes.Value.ProcessingStatus);

        // 1. Verify checkout session on PayMongo gateway was explicitly locked/expired
        Assert.Contains(csId, gateway.ExpiredSessionCalls);

        // 2. Verify SignalR notification was published for attemptId with status "failed"
        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == attemptId && e.Status == "failed");

        // 3. Verify local query confirms attempt is Failed and CheckoutUrl is locked (null)
        var polled = await pollHandler.HandleAsync(new PollAndReconcileQuery(attemptId, AutoReconcile: false));
        Assert.True(polled.IsSuccess);
        Assert.Equal("failed", polled.Value.Status);
        Assert.Null(polled.Value.CheckoutUrl);
    }

    // =========================================================================
    // Scenario 8: qr_code.expired Webhook Locks Checkout Session & Notifies SignalR
    // =========================================================================
    [Fact]
    public async Task Scenario08_QrCodeExpiredWebhook_LocksRemoteSession_MarksExpired_And_NotifiesSignalR()
    {
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "qrph", "idem-qr-expire-test"));
        string csId = attRes.Value.ProviderPaymentId!;
        string attemptId = attRes.Value.Id;

        long unixNow = clock.UtcNow.ToUnixTimeSeconds();
        string rawPayload = $$"""
            {
              "data": {
                "id": "evt_qr_expire_001",
                "type": "event",
                "attributes": {
                  "type": "qr_code.expired",
                  "data": {
                    "id": "qr_expired_999",
                    "type": "qr_code",
                    "attributes": {
                      "status": "expired",
                      "metadata": {
                        "payment_attempt_id": "{{attemptId}}",
                        "order_id": "{{ordRes.Value.Id}}",
                        "idempotency_key": "idem-qr-expire-test"
                      }
                    }
                  }
                }
              }
            }
            """;

        string sigHeader = WebhookSigner.CreateSignatureHeader(rawPayload, WebhookSecret, unixNow);
        var webhookRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "corr-qr-1"));

        Assert.True(webhookRes.IsSuccess);
        Assert.Equal("processed", webhookRes.Value.ProcessingStatus);
        Assert.Equal(attemptId, webhookRes.Value.PaymentAttemptId);

        // 1. Verify checkout session on PayMongo gateway was explicitly locked/expired
        Assert.Contains(csId, gateway.ExpiredSessionCalls);

        // 2. Verify SignalR notification was published for attemptId with status "expired"
        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == attemptId && e.Status == "expired");

        // 3. Verify local query confirms attempt is Expired and CheckoutUrl is null
        var polled = await pollHandler.HandleAsync(new PollAndReconcileQuery(attemptId, AutoReconcile: false));
        Assert.True(polled.IsSuccess);
        Assert.Equal("expired", polled.Value.Status);
        Assert.Null(polled.Value.CheckoutUrl);
    }

    // =========================================================================
    // Scenario 9: Succeeded Payment Without Prior Receipt -> Just-In-Time Synthesis
    // =========================================================================
    [Fact]
    public async Task Scenario09_PaidPaymentWithoutPriorReceipt_SynthesizesReceiptJustInTime()
    {
        var (sp, clock, _, _) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var receiptRepo = scope.ServiceProvider.GetRequiredService<Rge.Receipts.Application.Ports.IReceiptRepository>();
        var getReceiptByPayment = scope.ServiceProvider.GetRequiredService<Rge.Receipts.Application.GetReceiptByPayment.GetReceiptByPaymentHandler>();
        var getReceiptByOrder = scope.ServiceProvider.GetRequiredService<Rge.Receipts.Application.GetReceiptByOrder.GetReceiptByOrderHandler>();
        var renderPdf = scope.ServiceProvider.GetRequiredService<Rge.Receipts.Application.RenderReceiptPdf.RenderReceiptPdfHandler>();

        // 1. Create Order & mark payment attempt succeeded without running OutboxWorker
        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("ADV-TAX-TEXT-2026", Quantity: 1));
        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "card", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-jit-receipt-1", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_paid_jit_test", "https://checkout.paymongo.com/cs_paid_jit_test", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        attempt.MarkSucceeded(clock.UtcNow, "card");
        await attemptRepo.SaveAsync(attempt);

        Order? order = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        order!.MarkPaid(clock.UtcNow);
        await orderRepo.SaveAsync(order);

        // 2. Verify NO receipt exists in repository before query
        var existingBefore = await receiptRepo.GetByPaymentAttemptIdAsync(attempt.Id);
        Assert.Null(existingBefore);

        // 3. Query GET /receipt by payment attempt -> Triggers Just-In-Time synthesis!
        var receiptResult = await getReceiptByPayment.HandleAsync(new Rge.Receipts.Application.GetReceiptByPayment.GetReceiptByPaymentQuery(attempt.Id));
        Assert.True(receiptResult.IsSuccess);
        Assert.NotNull(receiptResult.Value);
        Assert.StartsWith("OR-", receiptResult.Value.OfficialReceiptNumber, StringComparison.Ordinal);
        Assert.Equal(ordRes.Value.AmountMinor, receiptResult.Value.GrossAmountMinor);
        Assert.False(string.IsNullOrWhiteSpace(receiptResult.Value.AuditDigest));

        // 4. Verify receipt is now persisted in repository
        var existingAfter = await receiptRepo.GetByPaymentAttemptIdAsync(attempt.Id);
        Assert.NotNull(existingAfter);
        Assert.Equal(receiptResult.Value.OfficialReceiptNumber, existingAfter.ReceiptNumber.Value);

        // 5. Query GET /receipt by order ID -> Returns identical receipt
        var orderReceiptResult = await getReceiptByOrder.HandleAsync(new Rge.Receipts.Application.GetReceiptByOrder.GetReceiptByOrderQuery(ordRes.Value.Id));
        Assert.True(orderReceiptResult.IsSuccess);
        Assert.Equal(receiptResult.Value.OfficialReceiptNumber, orderReceiptResult.Value.OfficialReceiptNumber);

        // 6. Direct PDF rendering for payment ID succeeds
        var pdfResult = await renderPdf.HandleAsync(new Rge.Receipts.Application.RenderReceiptPdf.RenderReceiptPdfQuery(PaymentId: attempt.Id));
        Assert.True(pdfResult.IsSuccess);
        Assert.True(pdfResult.Value.PdfBytes.Length > 0);
        Assert.Equal("application/pdf", pdfResult.Value.ContentType);
    }

    // =========================================================================
    // Scenario 10: Polling with Underlying Source Expired -> DB Expired, URL Locked, SignalR Dispatched
    // =========================================================================
    [Fact]
    public async Task Scenario10_PollWithUnderlyingSourceExpired_UpdatesDb_CallsExpire_And_NotifiesSignalR()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "gcash", "idem-src-exp-test"));
        string csId = attRes.Value.ProviderPaymentId!;
        string attemptId = attRes.Value.Id;

        // Simulate gateway returning status "expired" (as evaluated from underlying source inspection)
        gateway.SetRemoteSessionStatus(csId, "expired", "gcash", failureCode: "source_expired", failureMessage: "Source expired");

        // Act: Poll with auto_reconcile = true
        var polled = await pollHandler.HandleAsync(new PollAndReconcileQuery(attemptId, AutoReconcile: true));

        // Assert 1: Polled DTO reflects Expired and nullifies CheckoutUrl
        Assert.True(polled.IsSuccess);
        Assert.Equal("expired", polled.Value.Status.ToLowerInvariant());
        Assert.Null(polled.Value.CheckoutUrl);

        // Assert 2: Gateway session invalidation was triggered remotely
        for (int i = 0; i < 50 && !gateway.ExpiredSessionCalls.Contains(csId); i++)
        {
            await Task.Delay(20);
        }
        Assert.Contains(csId, gateway.ExpiredSessionCalls);

        // Assert 3: SignalR notification was dispatched for both attemptId and csId
        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == attemptId && e.Status == "expired");
        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == csId && e.Status == "expired");
    }

    // =========================================================================
    // Scenario 11: Official PayMongo qrph.expired Webhook -> Invalidation & SignalR
    // =========================================================================
    [Fact]
    public async Task Scenario11_QrphExpiredWebhook_MatchesOfficialPayMongoEvent_And_MarksExpired()
    {
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("ADV-TAX-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "qrph", "idem-qrph-exp"));
        string csId = attRes.Value.ProviderPaymentId!;
        string attemptId = attRes.Value.Id;

        long unixNow = clock.UtcNow.ToUnixTimeSeconds();
        string rawPayload = $$"""
            {
              "data": {
                "id": "evt_qrph_exp_official_001",
                "type": "event",
                "attributes": {
                  "type": "qrph.expired",
                  "data": {
                    "id": "{{csId}}",
                    "type": "checkout_session",
                    "attributes": {
                      "status": "expired",
                      "metadata": {
                        "payment_attempt_id": "{{attemptId}}"
                      }
                    }
                  }
                }
              }
            }
            """;

        string sigHeader = WebhookSigner.CreateSignatureHeader(rawPayload, WebhookSecret, unixNow);
        var webhookRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "corr-qrph-exp"));
        Assert.True(webhookRes.IsSuccess);

        PaymentAttempt? attempt = await attemptRepo.GetByIdAsync(attemptId);
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);

        for (int i = 0; i < 50 && !gateway.ExpiredSessionCalls.Contains(csId); i++)
        {
            await Task.Delay(20);
        }
        Assert.Contains(csId, gateway.ExpiredSessionCalls);
        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == attemptId && e.Status == "expired");
    }

    // =========================================================================
    // Scenario 12: payment.paid with pay_... correlates via source.id
    // =========================================================================
    [Fact]
    public async Task Scenario12_PaymentPaidWebhook_WithPayResourceId_CorrelatesViaSourceId()
    {
        var (sp, clock, _, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-src-correlate", clock.UtcNow);
        string srcId = "src_paymongo_source_9999";
        attempt.AttachCheckoutSession(srcId, "https://checkout.paymongo.com/src_9999", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        long unixNow = clock.UtcNow.ToUnixTimeSeconds();
        string rawPayload = $$"""
            {
              "data": {
                "id": "evt_pay_src_rel_001",
                "type": "event",
                "attributes": {
                  "type": "payment.paid",
                  "data": {
                    "id": "pay_live_settled_555",
                    "type": "payment",
                    "attributes": {
                      "status": "paid",
                      "payment_method_used": "gcash",
                      "source": {
                        "id": "{{srcId}}",
                        "type": "source"
                      }
                    }
                  }
                }
              }
            }
            """;

        string sigHeader = WebhookSigner.CreateSignatureHeader(rawPayload, WebhookSecret, unixNow);
        var webhookRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "corr-src-rel"));
        Assert.True(webhookRes.IsSuccess);

        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);

        Order? updatedOrder = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        Assert.NotNull(updatedOrder);
        Assert.Equal("paid", updatedOrder.Status);

        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == attempt.Id && e.Status == "succeeded");
    }

    // =========================================================================
    // Scenario 13: Polling at deadline bypasses cooldown and authoritatively verifies paid status
    // =========================================================================
    [Fact]
    public async Task Scenario13_PollingAtDeadline_BypassesCooldown_And_DetectsLatePayment()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "qrph", "idem-deadline-cd"));
        string csId = attRes.Value.ProviderPaymentId!;
        string attemptId = attRes.Value.Id;

        // 1. Advance to 1 second before deadline (T+29m59s) and poll to trigger cooldown
        clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
        gateway.SetRemoteSessionStatus(csId, "active");
        var prePoll = await pollHandler.HandleAsync(new PollAndReconcileQuery(attemptId, AutoReconcile: true));
        Assert.True(prePoll.IsSuccess);
        Assert.Equal("requiresaction", prePoll.Value.Status.ToLowerInvariant());

        // 2. Advance 2 seconds (now at T+30m01s, past the 30-min deadline, but within 3-sec cooldown!)
        clock.Advance(TimeSpan.FromSeconds(2));
        gateway.SetRemoteSessionStatus(csId, "paid", "qrph", clock.UtcNow);

        // 3. Poll at deadline: Even though within 3-sec cooldown window, deadline check forces authoritative gateway query!
        var deadlinePoll = await pollHandler.HandleAsync(new PollAndReconcileQuery(attemptId, AutoReconcile: true));
        Assert.True(deadlinePoll.IsSuccess);
        Assert.Equal("succeeded", deadlinePoll.Value.Status.ToLowerInvariant());

        PaymentAttempt? attempt = await attemptRepo.GetByIdAsync(attemptId);
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Contains(notifier.PublishedEvents, e => e.AttemptId == attemptId && e.Status == "succeeded");
    }

    // =========================================================================
    // Scenario 14: State Locking — Succeeded Attempt Locked Against Concurrent Poll or Late Webhook
    // =========================================================================
    [Fact]
    public async Task Scenario14_SucceededAttempt_LockedAgainstConcurrentPollOrLateExpiredWebhook()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, _) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        var attRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "qrph", "idem-lock-001"));
        string csId = attRes.Value.ProviderPaymentId!;
        string attemptId = attRes.Value.Id;

        // 1. Settle attempt to Succeeded
        PaymentAttempt? attempt = await attemptRepo.GetByIdAsync(attemptId);
        Assert.NotNull(attempt);
        attempt.MarkSucceeded(clock.UtcNow, "qrph");
        await attemptRepo.SaveAsync(attempt);

        // 2. Set remote session status to expired
        gateway.SetRemoteSessionStatus(csId, "expired", failureCode: "source_expired");

        // 3. Poll and reconcile: must remain Succeeded
        var pollRes = await pollHandler.HandleAsync(new PollAndReconcileQuery(attemptId, AutoReconcile: true));
        Assert.True(pollRes.IsSuccess);
        Assert.Equal("succeeded", pollRes.Value.Status.ToLowerInvariant());

        PaymentAttempt? reloaded = await attemptRepo.GetByIdAsync(attemptId);
        Assert.NotNull(reloaded);
        Assert.Equal(PaymentAttemptStatus.Succeeded, reloaded.Status);
        Assert.NotNull(reloaded.PaidAt);
        Assert.Null(reloaded.FailureCode);

        // 4. Late qrph.expired webhook arrives: must not flip Succeeded
        long unixNow = clock.UtcNow.ToUnixTimeSeconds();
        string rawPayload = $$"""
            {
              "data": {
                "id": "evt_late_expired_001",
                "type": "event",
                "attributes": {
                  "type": "qrph.expired",
                  "data": {
                    "id": "{{csId}}",
                    "type": "checkout_session",
                    "attributes": {
                      "status": "expired"
                    }
                  }
                }
              }
            }
            """;

        string sigHeader = WebhookSigner.CreateSignatureHeader(rawPayload, WebhookSecret, unixNow);
        var webhookRes = await receiveWebhook.HandleAsync(new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "late-webhook-rel"));
        Assert.True(webhookRes.IsSuccess);

        PaymentAttempt? postWebhook = await attemptRepo.GetByIdAsync(attemptId);
        Assert.NotNull(postWebhook);
        Assert.Equal(PaymentAttemptStatus.Succeeded, postWebhook.Status);
    }

    // =========================================================================
    // Scenario 15: Double-Capture Race Protection & Verification Recovery
    // =========================================================================
    [Fact]
    public async Task Scenario15_DoubleCaptureRace_SimultaneousWebhookAndPoll_RecoversToSucceededWithoutFlipping()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, notifier) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        string srcId = "src_dbl_capture_001";

        var attempt = PaymentAttempt.CreatePending(
            ordRes.Value.Id, "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes.Value.AmountMinor), "idem-dbl-001", clock.UtcNow);
        attempt.AttachCheckoutSession(srcId, $"https://checkout.paymongo.com/{srcId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        attempt.MarkProcessing(clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        // Remote gateway shows paid (captured by concurrent webhook)
        gateway.SetRemoteSessionStatus(srcId, "paid", "gcash", clock.UtcNow);

        // Poll query executes
        var pollRes = await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt.Id, AutoReconcile: true));
        Assert.True(pollRes.IsSuccess);
        Assert.Equal("succeeded", pollRes.Value.Status.ToLowerInvariant());

        PaymentAttempt? reloaded = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(PaymentAttemptStatus.Succeeded, reloaded.Status);

        Order? updatedOrder = await orderRepo.GetByIdAsync(ordRes.Value.Id);
        Assert.NotNull(updatedOrder);
        Assert.Equal("paid", updatedOrder.Status);
    }

    // =========================================================================
    // Scenario 16: Source Status Mapping Invariants (Cancelled -> Failed, Expired -> Expired)
    // =========================================================================
    [Fact]
    public async Task Scenario16_PayMongoSourceStatusMapping_MapsCancelledToFailed_And_ExpiredToExpired()
    {
        PollAndReconcileHandler.ResetCooldowns();
        var (sp, clock, gateway, _) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        // Part A: Cancelled source maps to Failed
        var ordRes1 = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        string srcCancelledId = "src_cancel_test_001";
        var attempt1 = PaymentAttempt.CreatePending(
            ordRes1.Value.Id, "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes1.Value.AmountMinor), "idem-cancel-001", clock.UtcNow);
        attempt1.AttachCheckoutSession(srcCancelledId, $"https://checkout.paymongo.com/{srcCancelledId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt1);

        gateway.SetRemoteSessionStatus(srcCancelledId, "cancelled", failureCode: "source_cancelled", failureMessage: "Source cancelled by user.");
        var pollCancelRes = await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt1.Id, AutoReconcile: true, ClientReportedRedirectStatus: "cancelled"));
        Assert.True(pollCancelRes.IsSuccess);
        Assert.Equal("failed", pollCancelRes.Value.Status.ToLowerInvariant());

        PaymentAttempt? reloaded1 = await attemptRepo.GetByIdAsync(attempt1.Id);
        Assert.NotNull(reloaded1);
        Assert.Equal(PaymentAttemptStatus.Failed, reloaded1.Status);
        Assert.Equal("source_cancelled", reloaded1.FailureCode);

        // Part B: Expired source maps to Expired
        PollAndReconcileHandler.ResetCooldowns();
        var ordRes2 = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        string srcExpiredId = "src_expire_test_001";
        var attempt2 = PaymentAttempt.CreatePending(
            ordRes2.Value.Id, "grab_pay", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(ordRes2.Value.AmountMinor), "idem-exp-002", clock.UtcNow);
        attempt2.AttachCheckoutSession(srcExpiredId, $"https://checkout.paymongo.com/{srcExpiredId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt2);

        gateway.SetRemoteSessionStatus(srcExpiredId, "expired", failureCode: "source_expired", failureMessage: "Source expired.");
        var pollExpireRes = await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt2.Id, AutoReconcile: true));
        Assert.True(pollExpireRes.IsSuccess);
        Assert.Equal("expired", pollExpireRes.Value.Status.ToLowerInvariant());

        PaymentAttempt? reloaded2 = await attemptRepo.GetByIdAsync(attempt2.Id);
        Assert.NotNull(reloaded2);
        Assert.Equal(PaymentAttemptStatus.Expired, reloaded2.Status);
        Assert.Equal("source_expired", reloaded2.FailureCode);
    }

    // =========================================================================
    // Scenario 17: Expiration Worker State Exclusions — Processing Attempts Are Never Swept
    // =========================================================================
    [Fact]
    public async Task Scenario17_ExpirationWorker_ProcessingAttempts_AreNeverSweptOrExpired()
    {
        var (sp, clock, gateway, _) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var worker = scope.ServiceProvider.GetRequiredService<PaymentExpirationWorker>();

        // 1. Attempt 1: In Processing status, deadline reached 30 minutes ago
        var attempt1 = PaymentAttempt.CreatePending(
            "ord-proc-001", "qrph", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(1000_00), "idem-proc-001", clock.UtcNow.AddHours(-1));
        attempt1.AttachCheckoutSession("cs_proc_001", "https://checkout.paymongo.com/cs_proc_001", clock.UtcNow.AddMinutes(-30), clock.UtcNow.AddHours(-1));
        attempt1.MarkProcessing(clock.UtcNow.AddMinutes(-30));
        await attemptRepo.SaveAsync(attempt1);

        // 2. Attempt 2: Older attempt on ord-proc-002, also in Processing
        var attempt2 = PaymentAttempt.CreatePending(
            "ord-proc-002", "gcash", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(2000_00), "idem-proc-002", clock.UtcNow.AddHours(-1));
        attempt2.AttachCheckoutSession("cs_proc_002", "https://checkout.paymongo.com/cs_proc_002", clock.UtcNow.AddMinutes(-10), clock.UtcNow.AddHours(-1));
        attempt2.MarkProcessing(clock.UtcNow.AddMinutes(-10));
        await attemptRepo.SaveAsync(attempt2);

        // 3. Attempt 3: Eligible RequiresAction attempt past deadline
        var attempt3 = PaymentAttempt.CreatePending(
            "ord-proc-003", "maya", Rge.BuildingBlocks.SharedKernel.Money.Money.Php(3000_00), "idem-proc-003", clock.UtcNow.AddHours(-1));
        attempt3.AttachCheckoutSession("cs_eligible_003", "https://checkout.paymongo.com/cs_eligible_003", clock.UtcNow.AddMinutes(-5), clock.UtcNow.AddHours(-1));
        await attemptRepo.SaveAsync(attempt3);
        gateway.SetRemoteSessionStatus("cs_eligible_003", "active");

        // Act: Run expiration worker sweep
        int processed = await worker.ProcessOverdueBatchOnceAsync();

        // Assert:
        Assert.Equal(1, processed);
        Assert.Contains("cs_eligible_003", gateway.ExpiredSessionCalls);
        Assert.DoesNotContain("cs_proc_001", gateway.ExpiredSessionCalls);
        Assert.DoesNotContain("cs_proc_002", gateway.ExpiredSessionCalls);

        PaymentAttempt? reloaded1 = await attemptRepo.GetByIdAsync(attempt1.Id);
        Assert.NotNull(reloaded1);
        Assert.Equal(PaymentAttemptStatus.Processing, reloaded1.Status);

        PaymentAttempt? reloaded2 = await attemptRepo.GetByIdAsync(attempt2.Id);
        Assert.NotNull(reloaded2);
        Assert.Equal(PaymentAttemptStatus.Processing, reloaded2.Status);

        PaymentAttempt? reloaded3 = await attemptRepo.GetByIdAsync(attempt3.Id);
        Assert.NotNull(reloaded3);
        Assert.Equal(PaymentAttemptStatus.Expired, reloaded3.Status);
    }

    // =========================================================================
    // Scenario 18: Initiating New Attempt Supersedes & Expires Prior Non-Terminal Attempts
    // =========================================================================
    [Fact]
    public async Task Scenario18_InitiateNewAttemptOnSameOrder_SupersedesAndExpiresPriorNonTerminalAttempts()
    {
        var (sp, clock, gateway, _) = BuildTestHarness();
        await using var _ = sp;
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var ordRes = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        Assert.True(ordRes.IsSuccess);

        // 1. Initiate first attempt (qrph)
        var firstRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "qrph", "idem-sup-001"));
        Assert.True(firstRes.IsSuccess);
        string firstAttemptId = firstRes.Value.Id;
        string firstCsId = firstRes.Value.ProviderPaymentId!;

        // 2. Customer changes payment method or retries on the same order -> initiates second attempt (gcash)
        clock.Advance(TimeSpan.FromMinutes(1));
        var secondRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ordRes.Value.Id, "gcash", "idem-sup-002"));
        Assert.True(secondRes.IsSuccess);
        string secondAttemptId = secondRes.Value.Id;
        string secondCsId = secondRes.Value.ProviderPaymentId!;

        // 3. First attempt must be Expired with superseded_by_new_attempt and its PayMongo session expired
        PaymentAttempt? firstReloaded = await attemptRepo.GetByIdAsync(firstAttemptId);
        Assert.NotNull(firstReloaded);
        Assert.Equal(PaymentAttemptStatus.Expired, firstReloaded.Status);
        Assert.Equal("superseded_by_new_attempt", firstReloaded.FailureCode);

        for (int i = 0; i < 50 && !gateway.ExpiredSessionCalls.Contains(firstCsId); i++)
        {
            await Task.Delay(20);
        }
        Assert.Contains(firstCsId, gateway.ExpiredSessionCalls);
        Assert.DoesNotContain(secondCsId, gateway.ExpiredSessionCalls);

        // 4. Second attempt remains active in RequiresAction
        PaymentAttempt? secondReloaded = await attemptRepo.GetByIdAsync(secondAttemptId);
        Assert.NotNull(secondReloaded);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, secondReloaded.Status);
    }
}

