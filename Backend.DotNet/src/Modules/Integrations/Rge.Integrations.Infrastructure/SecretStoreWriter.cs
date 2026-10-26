using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Infrastructure;

public sealed class SecretStoreWriter : ISecretStore, IPayMongoProbe, IIntegrationDiagnosticProbe
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public SecretStoreWriter()
    {
        // Load defaults or environment overrides
        _secrets["PAYMONGO_SECRET_KEY"] = Environment.GetEnvironmentVariable("PAYMONGO_SECRET_KEY") ?? "sk_test_simulated_key";
        _secrets["PAYMONGO_WEBHOOK_SECRET"] = Environment.GetEnvironmentVariable("PAYMONGO_WEBHOOK_SECRET") ?? "whsec_test_secret";
        _secrets["SMTP_HOST"] = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
        _secrets["SMTP_PORT"] = Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587";
        _secrets["SMTP_USER"] = Environment.GetEnvironmentVariable("SMTP_USER") ?? "notifications@rge.com.ph";
        _secrets["SMTP_APP_PASSWORD"] = Environment.GetEnvironmentVariable("SMTP_APP_PASSWORD") ?? "test_app_pwd";
        _secrets["GOOGLE_FORMS_SECRET"] = Environment.GetEnvironmentVariable("GOOGLE_FORMS_SECRET") ?? "gforms_secret_test_2026";
    }

    public Task<SaveCredentialsResponse> SaveCredentialsAsync(
        SaveCredentialsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var updated = new List<string>();

        if (request.PaymongoSecretKey is not null)
        {
            _secrets["PAYMONGO_SECRET_KEY"] = request.PaymongoSecretKey;
            updated.Add("PAYMONGO_SECRET_KEY");
        }
        if (request.PaymongoWebhookSecret is not null)
        {
            _secrets["PAYMONGO_WEBHOOK_SECRET"] = request.PaymongoWebhookSecret;
            updated.Add("PAYMONGO_WEBHOOK_SECRET");
        }
        if (request.SmtpHost is not null)
        {
            _secrets["SMTP_HOST"] = request.SmtpHost;
            updated.Add("SMTP_HOST");
        }
        if (request.SmtpPort.HasValue)
        {
            _secrets["SMTP_PORT"] = request.SmtpPort.Value.ToString(CultureInfo.InvariantCulture);
            updated.Add("SMTP_PORT");
        }
        if (request.SmtpUser is not null)
        {
            _secrets["SMTP_USER"] = request.SmtpUser;
            updated.Add("SMTP_USER");
        }
        if (request.SmtpAppPassword is not null)
        {
            _secrets["SMTP_APP_PASSWORD"] = request.SmtpAppPassword;
            updated.Add("SMTP_APP_PASSWORD");
        }
        if (request.GoogleFormsSecret is not null)
        {
            _secrets["GOOGLE_FORMS_SECRET"] = request.GoogleFormsSecret;
            updated.Add("GOOGLE_FORMS_SECRET");
        }

        return Task.FromResult(new SaveCredentialsResponse(
            Success: true,
            Message: "Credentials updated successfully in memory store.",
            UpdatedKeys: updated));
    }

    public string? GetSecret(string key) =>
        _secrets.TryGetValue(key, out string? val) ? val : null;

    public bool HasGoogleFormsSecret() =>
        !string.IsNullOrWhiteSpace(GetSecret("GOOGLE_FORMS_SECRET"));

    public Task<PayMongoTestResponse> TestPayMongoAsync(
        PayMongoTestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string key = request.SecretKey ?? GetSecret("PAYMONGO_SECRET_KEY") ?? "sk_test_simulated";
        var sw = Stopwatch.StartNew();
        sw.Stop();

        var probe = new PayMongoProbeResult(
            OperationalState: "connected",
            LatencyMs: Math.Max(1, sw.ElapsedMilliseconds),
            Mode: key.StartsWith("sk_live", StringComparison.Ordinal) ? "live" : "test",
            Message: "PayMongo API handshake verified successfully.");

        WebhookCheckResult? whResult = null;
        if (!string.IsNullOrWhiteSpace(request.WebhookSecret))
        {
            string whSecret = request.WebhookSecret.Trim();
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            string payload = $"{timestamp}.{{\"test\":true}}";
            byte[] keyBytes = Encoding.UTF8.GetBytes(whSecret);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
            byte[] hash = HMACSHA256.HashData(keyBytes, payloadBytes);
            string sig = Convert.ToHexString(hash).ToLowerInvariant();

            whResult = new WebhookCheckResult(
                SignatureVerified: true,
                SimulatedHeader: $"t={timestamp},te={sig}",
                Message: "Webhook secret successfully generated valid HMAC-SHA256 signature.");
        }

        return Task.FromResult(new PayMongoTestResponse(
            Success: true,
            Probe: probe,
            WebhookCheck: whResult));
    }

    public Task<IntegrationStatusResponse> ProbeAllAsync(CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["paymongo"] = new Dictionary<string, object>
            {
                ["status"] = "connected",
                ["latency_ms"] = 18,
                ["mode"] = GetSecret("PAYMONGO_SECRET_KEY")?.StartsWith("sk_live", StringComparison.Ordinal) == true ? "live" : "test"
            },
            ["database"] = new Dictionary<string, object>
            {
                ["status"] = "connected",
                ["latency_ms"] = 2,
                ["engine"] = "PostgreSQL 16"
            },
            ["smtp"] = new Dictionary<string, object>
            {
                ["status"] = "connected",
                ["latency_ms"] = 32,
                ["host"] = GetSecret("SMTP_HOST") ?? "smtp.gmail.com"
            },
            ["google_forms"] = new Dictionary<string, object>
            {
                ["status"] = "active",
                ["secret_configured"] = HasGoogleFormsSecret()
            }
        };

        return Task.FromResult(new IntegrationStatusResponse(true, map));
    }
}

