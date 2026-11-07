using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;
using Rge.Payments.Infrastructure.Persistence.Repositories;

namespace Rge.Payments.Integration.Tests;

public record CapturedLog(LogLevel Level, string Category, EventId EventId, string Message, Exception? Exception);

public sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentBag<CapturedLog> _logs;

    public TestLoggerProvider(ConcurrentBag<CapturedLog> logs) => _logs = logs;

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, _logs);

    public void Dispose() { }

    private sealed class TestLogger(string category, ConcurrentBag<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            logs.Add(new CapturedLog(logLevel, category, eventId, formatter(state, exception), exception));
        }
    }
}

/// <summary>
/// Controllable in-memory test payment gateway for integration test suites.
/// </summary>
public sealed class TestPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, GatewayPaymentStatusResult> _sessions = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentBag<string> ExpiredSessionCalls { get; } = [];
    public bool RejectNextExpireCallWith400 { get; set; }

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
        ArgumentNullException.ThrowIfNull(request);

        string id = $"cs_test_{Guid.NewGuid():N}";
        SetRemoteSessionStatus(id, "active");
        return Task.FromResult(Result.Success(new CheckoutSessionResult(
            ProviderPaymentId: id,
            CheckoutUrl: $"https://checkout.paymongo.com/test/{id}",
            Status: "active",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(30))));
    }

    public Task<Result<GatewayPaymentStatusResult>> GetCheckoutSessionStatusAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return Task.FromResult(Result.Failure<GatewayPaymentStatusResult>(
                Error.Validation("PayMongo.InvalidId", "Provider payment ID is required.")));
        }

        if (_sessions.TryGetValue(providerPaymentId, out GatewayPaymentStatusResult? status))
        {
            return Task.FromResult(Result.Success(status));
        }

        var defaultStatus = new GatewayPaymentStatusResult(
            ProviderPaymentId: providerPaymentId,
            Status: "active",
            PaymentMethodUsed: null,
            PaidAtUtc: null,
            FailureCode: null,
            FailureMessage: null);

        return Task.FromResult(Result.Success(defaultStatus));
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

/// <summary>
/// Boots Program in memory using OfflineFallbackDbDataSource and in-memory repository fallbacks
/// without requiring an external PostgreSQL instance or Docker.
/// </summary>
public sealed class AccountingWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestPaymentGateway Gateway { get; } = new();
    public ConcurrentBag<CapturedLog> CapturedLogs { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(new TestLoggerProvider(CapturedLogs));
        });

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = "whsec_test_mock_secret_key_12345678",
                ["Secrets:PayMongoSecretKey"] = "sk_test_mock_secret_key_12345678",
                ["Secrets:GoogleFormsSecret"] = "gf_test_mock_secret_12345678",
                ["Secrets:FieldEncryptionKeyBase64"] = Convert.ToBase64String(new byte[32]),
                ["Secrets:TaxPayerSecret"] = "tp_test_secret_12345678",
                ["ConnectionStrings:AccountingDb"] = "Host=offline;Database=rge_accounting_offline",
                ["FINANCE_SEED"] = "dev"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IPaymentGateway>(Gateway));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        OrderRepository.ResetToBaseline();
    }
}


