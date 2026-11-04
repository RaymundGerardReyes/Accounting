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
public sealed class PayMongoLifecycleVerificationTests
{
    private const string WebhookSecret = "whsec_lifecycle_proof_secret_2026";

    // =========================================================================
    // Test Doubles: Controllable PayMongo Gateway & SignalR Spy Notifier
    // =========================================================================

    private sealed class FakePayMongoGateway : IPaymentGateway
    {
        private readonly ConcurrentDictionary<string, GatewayPaymentStatusResult> _sessions = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentBag<string> ExpiredSessionCalls { get; } = [];
        public ConcurrentBag<string> StatusCalls { get; } = [];
        public bool RejectNextExpireCallWith400 { get; set; }

        public void SetRemoteSessionStatus(
            string checkoutSessionId,
            string status,
            string? paymentMethodUsed = "qrph",
            DateTimeOffset? paidAtUtc = null)
        {
            _sessions[checkoutSessionId] = new GatewayPaymentStatusResult(
                ProviderPaymentId: checkoutSessionId,
                Status: status,
                PaymentMethodUsed: paymentMethodUsed,
                PaidAtUtc: paidAtUtc,
                FailureCode: null,
                FailureMessage: null);
        }

        public Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(
            CreateCheckoutSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            string csId = $"cs_test_{Guid.NewGuid():N}"[..24];
            DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

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
        var gateway = new FakePayMongoGateway();
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
}

