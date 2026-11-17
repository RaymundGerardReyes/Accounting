using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.Accounting.Host.Composition;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.ResendReceiptEmail;
using Xunit;

namespace Rge.Receipts.Application.Tests;

public sealed class ReceiptApplicationWorkflowTests
{
    private static ServiceProvider CreateHostProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = "whsec_receipt_app_test",
                ["Secrets:FieldEncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task IssueReceiptPersistsAnOfficialReceipt()
    {
        await using ServiceProvider provider = CreateHostProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        IssueReceiptHandler handler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        IReceiptRepository repository = scope.ServiceProvider.GetRequiredService<IReceiptRepository>();
        string paymentId = $"pay_receipt_test_{Guid.NewGuid():N}";

        Result<ReceiptDto> result = await handler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-202611-0500",
            PaymentAttemptId: paymentId,
            AmountMinor: 112_000L,
            Currency: "PHP",
            CustomerName: "Juan Dela Cruz",
            CustomerEmail: "juan.delacruz@rge.com.ph",
            Description: "Receipt application test",
            PaymentMethod: "qrph"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(await repository.GetByPaymentAttemptIdAsync(paymentId));
    }

    [Fact]
    public async Task ResendReceiptReturnsNotFoundForUnknownPayment()
    {
        await using ServiceProvider provider = CreateHostProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        ResendReceiptEmailHandler handler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();

        Result<ResendReceiptEmailResponse> result =
            await handler.HandleAsync(new ResendReceiptEmailCommand("pay_missing_receipt"));

        Assert.True(result.IsFailure);
    }
}
