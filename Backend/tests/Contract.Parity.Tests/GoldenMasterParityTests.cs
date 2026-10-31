using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.Accounting.Host.Composition;
using Rge.Audit.Application.Ports;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Receipts.Application.GetReceiptByOrder;
using Rge.Receipts.Application.RenderReceiptPdf;
using Xunit;

namespace Rge.Contract.Parity.Tests;

public sealed class GoldenMasterParityTests
{
    private static ServiceProvider BuildPlatformServiceProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = "whsec_parity_test_secret",
                ["Secrets:GoogleFormsSecret"] = "rge_gforms_rotated_vault_secret_2026"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EndToEnd_Order_PayMongoWebhook_Outbox_BirOfficialReceipt_And_Pdf_Parity()
    {
        await using ServiceProvider sp = BuildPlatformServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var receiveWebhook = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var outboxWorker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();
        var getReceiptByOrder = scope.ServiceProvider.GetRequiredService<GetReceiptByOrderHandler>();
        var renderPdf = scope.ServiceProvider.GetRequiredService<RenderReceiptPdfHandler>();

        // 1. Create Order with Server-Authoritative Pricing (COST-ACC-TEXT-2026 = PHP 1,850.00 x 2 = PHP 3,700.00 / 370,000 centavos)
        var orderRes = await createOrder.HandleAsync(new CreateOrderCommand(
            Title: null,
            Category: "product_order",
            ReferenceCode: "COST-ACC-TEXT-2026",
            Quantity: 2,
            CustomerName: "Maria Clara Ñañez",
            CustomerEmail: "maria.clara@example.ph"));

        Assert.True(orderRes.IsSuccess);
        Assert.Equal(370_000L, orderRes.Value.AmountMinor);
        Assert.Equal("pending", orderRes.Value.Status);

        // 2. Initiate PaymentAttempt
        var attemptRes = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(
            OrderId: orderRes.Value.Id,
            PaymentMethod: "qrph",
            IdempotencyKey: "idem-parity-001"));

        Assert.True(attemptRes.IsSuccess);
        Assert.Equal("requiresaction", attemptRes.Value.Status);

        // 3. Dispatch Signed PayMongo Paid Webhook
        long unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string rawPayload = $$"""
            {
              "data": {
                "id": "evt_parity_0001",
                "attributes": {
                  "type": "checkout_session.payment.paid",
                  "data": {
                    "id": "{{attemptRes.Value.ProviderPaymentId}}",
                    "attributes": {
                      "payment_attempt_id": "{{attemptRes.Value.Id}}",
                      "payment_method_used": "qrph"
                    }
                  }
                }
              }
            }
            """;

        byte[] sigBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("whsec_parity_test_secret"),
            Encoding.UTF8.GetBytes($"{unixNow}.{rawPayload}"));
        string sigHeader = $"t={unixNow},te={Convert.ToHexStringLower(sigBytes)}";

        var webhookRes = await receiveWebhook.HandleAsync(
            new ReceivePayMongoWebhookCommand(sigHeader, rawPayload, "corr-parity-001"));

        Assert.True(webhookRes.IsSuccess);
        Assert.Equal("processed", webhookRes.Value.Status);

        // 4. Process Transactional Outbox -> Issues BIR Official Receipt & Pre-Warms PDF
        int processedCount = await outboxWorker.ProcessNextBatchOnceAsync();
        Assert.Equal(1, processedCount);

        // 5. Verify BIR RMC 77-2024 Official Receipt & 12% VAT Breakdown
        var receiptRes = await getReceiptByOrder.HandleAsync(new GetReceiptByOrderQuery(orderRes.Value.Id));
        Assert.True(receiptRes.IsSuccess);
        Assert.StartsWith("OR-", receiptRes.Value.OfficialReceiptNumber, StringComparison.Ordinal);
        Assert.StartsWith("ACK-", receiptRes.Value.TreasuryAcknowledgementNumber, StringComparison.Ordinal);
        Assert.StartsWith("sha256:v2:", receiptRes.Value.AuditDigest, StringComparison.Ordinal);
        Assert.Equal(370_000L, receiptRes.Value.GrossAmountMinor);
        Assert.Equal(370_000L, receiptRes.Value.VatableSalesMinor + receiptRes.Value.VatAmountMinor);

        // 6. Verify Vector PDF (%PDF-1.4) Stream
        var pdfRes = await renderPdf.HandleAsync(new RenderReceiptPdfQuery(OrderId: orderRes.Value.Id));
        Assert.True(pdfRes.IsSuccess);
        Assert.True(pdfRes.Value.PdfBytes.Length > 250);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfRes.Value.PdfBytes, 0, 8));
    }

    [Fact]
    public async Task Cryptographic_Audit_HashChain_Detects_Zero_Tampering()
    {
        await using ServiceProvider sp = BuildPlatformServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var auditStore = scope.ServiceProvider.GetRequiredService<IAuditStore>();

        await auditStore.AppendAsync(
            "emp_field_01", "employee", "advance_submitted", "cash_advance", "adv_01",
            "{}", "{\"status\":\"submitted\"}", "Field visit", "corr_01", null);

        await auditStore.AppendAsync(
            "sup_mfg_01", "supervisor", "advance_approved", "cash_advance", "adv_01",
            "{\"status\":\"submitted\"}", "{\"status\":\"approved\"}", "Approved within budget", "corr_01", null);

        ChainVerificationReport report = await auditStore.VerifyChainAsync();
        Assert.True(report.IsIntact);
        Assert.True(report.TotalVerifiedRecords >= 2);
    }
}

