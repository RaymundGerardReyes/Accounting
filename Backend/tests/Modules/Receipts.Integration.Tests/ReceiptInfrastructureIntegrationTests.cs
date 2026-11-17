using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rge.Accounting.Host.Composition;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.ResendReceiptEmail;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Mail;
using Xunit;

namespace Rge.Receipts.Integration.Tests;

public sealed class RecordingReceiptMailer : IReceiptMailer
{
    public ConcurrentBag<(OfficialReceipt Receipt, string Recipient, int PdfByteCount)> SentMessages { get; } = [];

    public Task<Result> SendReceiptEmailAsync(
        OfficialReceipt receipt,
        string recipientEmail,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        SentMessages.Add((receipt, recipientEmail, pdfBytes.Length));
        return Task.FromResult(Result.Success());
    }
}

public sealed class ReceiptInfrastructureIntegrationTests
{
    [Fact]
    public async Task BackgroundWorkerDispatchesQueuedReceiptEmail()
    {
        var recordingMailer = new RecordingReceiptMailer();
        var queue = new ChannelReceiptEmailQueue(capacity: 100);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = "whsec_receipt_int_test",
                ["Secrets:FieldEncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(configuration);
        services.AddSingleton<IReceiptEmailQueue>(queue);
        services.AddSingleton<IReceiptMailer>(recordingMailer);

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        IssueReceiptHandler issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        ResendReceiptEmailHandler resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();
        ILogger<ReceiptEmailBackgroundWorker> logger =
            scope.ServiceProvider.GetRequiredService<ILogger<ReceiptEmailBackgroundWorker>>();
        string paymentId = $"pay_receipt_int_{Guid.NewGuid():N}";

        Result<ReceiptDto> issueResult = await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-INT-2026-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 250_000L,
            Currency: "PHP",
            CustomerName: "Maria Santos",
            CustomerEmail: "maria.santos@rge.com.ph",
            Description: "Receipt integration test",
            PaymentMethod: "gcash"));
        Assert.True(issueResult.IsSuccess);

        Result<ResendReceiptEmailResponse> resendResult = await resendHandler.HandleAsync(
            new ResendReceiptEmailCommand(paymentId, "maria.santos@rge.com.ph", AwaitAcceptance: false));
        Assert.True(resendResult.IsSuccess);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var worker = new ReceiptEmailBackgroundWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger);

        await worker.StartAsync(cancellation.Token);
        for (int i = 0; i < 20 && recordingMailer.SentMessages.IsEmpty; i++)
        {
            await Task.Delay(25);
        }

        await cancellation.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(recordingMailer.SentMessages);
        (OfficialReceipt _, string recipient, int pdfByteCount) = recordingMailer.SentMessages.Single();
        Assert.Equal("maria.santos@rge.com.ph", recipient);
        Assert.True(pdfByteCount > 0);
    }
}
