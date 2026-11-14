using System.Net.Http.Headers;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Payments.Integration.Tests.E2E.Fixtures;

/// <summary>
/// Fluent authentication and identity extension methods for HttpClient
/// to simulate anonymous, user, admin, and finance_admin actors in E2E tests.
/// </summary>
public static class HttpClientAuthExtensions
{
    public const string DefaultAdminId = "acme_admin";
    public const string DefaultUserId = "user_e2e_student";
    public const string DefaultFinanceAdminId = "finance_officer_auditor";
    public const string DefaultGoogleFormsSecret = "gforms_secret_test_2026";

    public static HttpClient AsAnonymous(this HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.DefaultRequestHeaders.Remove("X-User-ID");
        client.DefaultRequestHeaders.Remove("X-Act-As-User-ID");
        client.DefaultRequestHeaders.Remove("X-User-Roles");
        client.DefaultRequestHeaders.Remove("X-Google-Forms-Secret");
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    public static HttpClient AsUser(this HttpClient client, string userId = DefaultUserId)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.AsAnonymous();
        client.DefaultRequestHeaders.Add("X-User-ID", userId);
        client.DefaultRequestHeaders.Add("X-User-Roles", "user");
        return client;
    }

    public static HttpClient AsAdmin(this HttpClient client, string adminId = DefaultAdminId)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.AsAnonymous();
        client.DefaultRequestHeaders.Add("X-User-ID", adminId);
        client.DefaultRequestHeaders.Add("X-User-Roles", "admin,finance_admin");
        return client;
    }

    public static HttpClient AsFinanceAdmin(this HttpClient client, string userId = DefaultFinanceAdminId)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.AsAnonymous();
        client.DefaultRequestHeaders.Add("X-User-ID", userId);
        client.DefaultRequestHeaders.Add("X-User-Roles", "finance_admin");
        return client;
    }

    public static HttpClient WithGoogleFormsSecret(this HttpClient client, string secret = DefaultGoogleFormsSecret)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.DefaultRequestHeaders.Remove("X-Google-Forms-Secret");
        if (!string.IsNullOrWhiteSpace(secret))
        {
            client.DefaultRequestHeaders.Add("X-Google-Forms-Secret", secret);
        }
        return client;
    }
}

/// <summary>
/// Capturing SMTP client factory test double that records cancellation tokens and verifies background async dispatch.
/// </summary>
public sealed class CapturingSmtpClientFactory : ISmtpClientFactory
{
    public TaskCompletionSource<CancellationToken> TokenCapturedSource { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<SmtpExecutionResult> SendPaymentInvitationEmailAsync(
        string orderId,
        string orderNumber,
        string title,
        long amountMinor,
        string currency,
        string? customerName,
        string? customerEmail,
        string checkoutUrl,
        string? googleFormResponseId = null,
        CancellationToken ct = default)
    {
        TokenCapturedSource.TrySetResult(ct);
        return Task.FromResult(new SmtpExecutionResult(true, 12, "Invitation email sent"));
    }

    public Task<EmailTestResponse> TestSmtpAsync(EmailTestRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailTestResponse(true, new SmtpProbeResult("OK", 5, "smtp.mock.test", "ok"), null));

    public Task<SmtpExecutionResult> VerifyHandshakeAsync(CancellationToken ct = default) =>
        Task.FromResult(new SmtpExecutionResult(true, 5, "handshake ok"));

    public Task<SmtpExecutionResult> SendVerificationEmailAsync(string? recipientEmail, CancellationToken ct = default) =>
        Task.FromResult(new SmtpExecutionResult(true, 5, "verification email sent"));

    public SmtpRuntimeSettings ResolveSettings() =>
        new SmtpRuntimeSettings("smtp.mock.test", 587, "mock_user", "mock_pass", "orders@test.com", "RGE Accounting", true, "treasury@test.com");
}
