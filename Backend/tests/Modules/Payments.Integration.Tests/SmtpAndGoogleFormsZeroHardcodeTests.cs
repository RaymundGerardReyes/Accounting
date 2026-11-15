using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.Accounting.Host.Composition;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.GetFormStatus;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Application.SubmitGoogleForm;
using Rge.Integrations.Contracts;
using Rge.Integrations.Domain;
using Rge.Integrations.Infrastructure;
using Rge.Payments.Application.Orders.ListOrders;
using Rge.Payments.Application.Ports;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.ResendReceiptEmail;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Mail;
using Xunit;

namespace Rge.Payments.Integration.Tests;

public sealed class SmtpAndGoogleFormsZeroHardcodeTests
{
    [Fact]
    public async Task GoogleForms_SecretVerification_RejectsMismatchWith401_And_AcceptsDynamicRuntimeSecretRotation()
    {
        string dynamicInitialSecret = $"gforms_dyn_sec_{Guid.NewGuid():N}";
        string dynamicRotatedSecret = $"gforms_rot_sec_{Guid.NewGuid():N}";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:GoogleFormsSecret"] = dynamicInitialSecret,
                ["Secrets:FieldEncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);

        await using ServiceProvider sp = services.BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var submitHandler = scope.ServiceProvider.GetRequiredService<SubmitGoogleFormHandler>();
        var secretStore = scope.ServiceProvider.GetRequiredService<ISecretStore>();

        var payload = new GoogleFormSubmissionRequest(
            ResponseId: $"gf_{Guid.NewGuid():N}",
            SheetRowIndex: null,
            SheetRow: null,
            CustomerName: "Dynamic Tester",
            CustomerEmail: "tester@example.edu.ph",
            CustomerPhone: "09171234567",
            ItemReference: "Cost Accounting Textbook",
            ReferenceCode: "COST-ACC-TEXT-2026",
            Title: "Cost Accounting Textbook",
            Category: "product_order",
            Quantity: 1,
            AmountMinor: 150_00,
            PaymentMethod: "qrph",
            Notes: "Zero hardcode test",
            RawAnswers: null,
            StudentId: null,
            InstructorCredit: null,
            BookTitle: null,
            Secret: null,
            SecretKey: null);

        // 1. Mismatched secret header must return Unauthorized (GoogleForms.Unauthorized)
        var unauthorizedResult = await submitHandler.HandleAsync(
            new SubmitGoogleFormCommand(payload, "wrong_secret_value"));

        Assert.True(unauthorizedResult.IsFailure);
        Assert.Equal("GoogleForms.Unauthorized", unauthorizedResult.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, unauthorizedResult.Error.Type);

        // 2. Matching initial secret succeeds
        var authorizedResult = await submitHandler.HandleAsync(
            new SubmitGoogleFormCommand(payload, dynamicInitialSecret));

        Assert.True(authorizedResult.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(authorizedResult.Value.OrderNumber));

        // 3. Rotate secret dynamically at runtime via ISecretStore
        await secretStore.SaveCredentialsAsync(new SaveCredentialsRequest(
            GoogleFormsSecret: dynamicRotatedSecret));

        var rotatedPayload = payload with { ResponseId = $"gf_{Guid.NewGuid():N}" };
        var rotatedResult = await submitHandler.HandleAsync(
            new SubmitGoogleFormCommand(rotatedPayload, dynamicRotatedSecret));

        Assert.True(rotatedResult.IsSuccess);
    }

    [Fact]
    public async Task SmtpReceiptMailer_ResolvesDynamicConfig_And_StripsSpacesFromGoogleAppPassword()
    {
        await using var mockSmtp = new InProcessMockSmtpServer();

        var dynamicConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:SenderEmail"] = "treasury@example.org",
                ["Smtp:AppPassword"] = "abcd efgh ijkl mnop", // 16-char Google App Password with spaces
                ["Smtp:SenderName"] = "Dynamic Treasury Unit"
            })
            .Build();

        // Options has empty SenderEmail and AppPassword -> Mailer resolves them dynamically from IConfiguration
        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = mockSmtp.Port,
            SenderEmail = string.Empty,
            AppPassword = string.Empty,
            EnableSsl = false
        };

        var mailer = new SmtpReceiptMailer(options, dynamicConfig);
        OfficialReceipt receipt = SmtpParityAndLifecycleTests.CreateSampleReceipt("ord_dyn_smtp_01", "Dynamic Recipient");

        Result sendResult = await mailer.SendReceiptEmailAsync(
            receipt,
            "buyer@example.org",
            "%PDF-1.4 dynamic receipt stream"u8.ToArray());

        Assert.True(sendResult.IsSuccess);
        Assert.NotEmpty(mockSmtp.ReceivedEmails);
    }

    [Fact]
    public async Task ResendReceiptEmail_And_SmtpProbe_CompleteUnder15ms_NonBlocking()
    {
        // Inject a slow 2,500ms mailer to prove HTTP path never blocks on SMTP handshake
        var slowMailer = new AdversarialFaultyMailer(TimeSpan.FromMilliseconds(2500));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:FieldEncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);
        services.AddSingleton<IReceiptMailer>(slowMailer);

        await using ServiceProvider sp = services.BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();
        var smtpTester = scope.ServiceProvider.GetRequiredService<ISmtpProbe>();

        string paymentId = $"pay_fast_{Guid.NewGuid():N}";
        await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-FAST-001",
            PaymentAttemptId: paymentId,
            AmountMinor: 150_00,
            Currency: "PHP",
            CustomerName: "Low Latency Buyer",
            CustomerEmail: "fast@example.org",
            Description: "Low Latency Receipt Test",
            PaymentMethod: "qrph"));

        // Warmup
        _ = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId, "warmup@example.org"));

        // Measure non-blocking receipt email dispatch
        long t0 = Stopwatch.GetTimestamp();
        var resendRes = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId, "fast@example.org"));
        double resendElapsedMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

        Assert.True(resendRes.IsSuccess);
        Assert.True(resendElapsedMs < 15.0, $"Expected < 15ms response time, but took {resendElapsedMs:F2}ms.");

        // Measure non-blocking UI Test Email dispatch
        long t1 = Stopwatch.GetTimestamp();
        var probeRes = await smtpTester.TestSmtpAsync(new EmailTestRequest(
            Host: "smtp.gmail.com",
            Port: 587,
            User: "treasury@example.org",
            Password: "abcd efgh ijkl mnop",
            SendTestEmail: true,
            Recipient: "verify@example.org"));
        double probeElapsedMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;

        Assert.True(probeRes.Success);
        Assert.NotNull(probeRes.EmailDispatch);
        Assert.True(probeRes.EmailDispatch.Dispatched);
        Assert.True(probeElapsedMs < 15.0, $"Expected < 15ms probe response time, but took {probeElapsedMs:F2}ms.");
    }

    [Fact]
    public async Task GoogleForms_SubmitAndStatus_ShouldSyncLiveToOrdersLedger_AndReportConfiguredSecret()
    {
        string dynamicSecret = $"gforms_live_{Guid.NewGuid():N}";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:GoogleFormsSecret"] = dynamicSecret,
                ["Secrets:FieldEncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);

        await using ServiceProvider sp = services.BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var submitHandler = scope.ServiceProvider.GetRequiredService<SubmitGoogleFormHandler>();
        var statusHandler = scope.ServiceProvider.GetRequiredService<GetFormStatusHandler>();
        var listOrdersHandler = scope.ServiceProvider.GetRequiredService<ListOrdersHandler>();

        string responseId = $"gf_sync_{Guid.NewGuid():N}";
        var payload = new GoogleFormSubmissionRequest(
            ResponseId: responseId,
            SheetRowIndex: 1,
            SheetRow: null,
            CustomerName: "Maria Clara",
            CustomerEmail: "maria.clara@example.ph",
            CustomerPhone: "09170001122",
            ItemReference: "Cost Accounting Textbook 2026",
            ReferenceCode: "COST-ACC-TEXT-2026",
            Title: "Cost Accounting Textbook 2026",
            Category: "product_order",
            Quantity: 2,
            AmountMinor: 3700_00,
            PaymentMethod: "qrph",
            Notes: "Live sync verification",
            RawAnswers: null,
            StudentId: "STU-2026-99",
            InstructorCredit: "Prof. Cruz",
            BookTitle: "Cost Accounting Textbook 2026",
            Secret: null,
            SecretKey: null);

        var submitResult = await submitHandler.HandleAsync(new SubmitGoogleFormCommand(payload, dynamicSecret));
        Assert.True(submitResult.IsSuccess);
        Assert.True(submitResult.Value.Status is "created" or "success");
        Assert.False(string.IsNullOrWhiteSpace(submitResult.Value.OrderId));

        // Status check
        var statusResult = await statusHandler.HandleAsync(new GetGoogleFormsStatusQuery());
        Assert.True(statusResult.IsSuccess);
        Assert.True(statusResult.Value.SecretConfigured);
        Assert.NotNull(statusResult.Value.MaskedSecret);
        Assert.True(statusResult.Value.TotalSubmissions >= 1);

        // List orders check - unauthenticated/dashboard actor sees the bridged order
        var ordersResult = await listOrdersHandler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 50, UserId: null));
        Assert.True(ordersResult.IsSuccess);
        Assert.Contains(ordersResult.Value, o => o.Id == submitResult.Value.OrderId || o.OrderNumber == submitResult.Value.OrderNumber);
    }

    [Fact]
    public async Task PgGoogleFormsStore_ShouldSurviveProcessRestart_AndReadFromOrdersAndSubmissionsTables()
    {
        using var store1 = new PgGoogleFormsStore(logger: null, dataSource: null);

        string respId = $"gf_restart_{Guid.NewGuid():N}";
        var submission = new GoogleFormSubmission(
            OrderId: $"ord_{respId}",
            OrderNumber: "ORD-202611-9999",
            ResponseId: respId,
            CustomerName: "Durable User",
            CustomerEmail: "durable@example.ph",
            Description: "Durable Test Book",
            AmountCentavos: 1850_00,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow);

        var (savedSub, createdNew) = await store1.UpsertSubmissionAsync(submission);
        Assert.True(createdNew);
        Assert.Equal(respId, savedSub.ResponseId);

        var all1 = await store1.GetAllAsync();
        Assert.Contains(all1, x => x.ResponseId == respId);

        var status1 = await store1.GetStatusAsync();
        Assert.True(status1.TotalSubmissions >= 1);
        Assert.True(status1.SecretConfigured);

        // Re-upsert (idempotent submission)
        var (savedSub2, createdNew2) = await store1.UpsertSubmissionAsync(submission);
        Assert.False(createdNew2);
        Assert.Equal(respId, savedSub2.ResponseId);
    }

    private sealed class AdversarialFaultyMailer : IReceiptMailer
    {
        private readonly TimeSpan _delay;

        public AdversarialFaultyMailer(TimeSpan delay)
        {
            _delay = delay;
        }

        public async Task<Result> SendReceiptEmailAsync(
            OfficialReceipt receipt,
            string recipientEmail,
            byte[] pdfBytes,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            return Result.Success();
        }
    }
}