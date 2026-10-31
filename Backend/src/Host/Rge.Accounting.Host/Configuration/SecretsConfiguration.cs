using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Rge.Accounting.Host.Configuration;

/// <summary>
/// Validates that all mission-critical secrets are provided via Vault or Environment Variables
/// and never hardcoded in appsettings.json. Fails fast at startup on missing or compromised secrets.
/// </summary>
public static class SecretsConfiguration
{
    private const string CompromisedLegacyGoogleFormsSecret = "rge_gforms_live_sec_89f3a21";

    public static void ValidateRequiredSecretsOrThrow(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var missingOrInvalid = new List<string>();

        RequireSecret(configuration, "ConnectionStrings:AccountingDb", ["ConnectionStrings__AccountingDb", "DATABASE_URL"], missingOrInvalid);
        RequireSecret(configuration, "Secrets:PayMongoWebhookSecret", ["Secrets__PayMongoWebhookSecret", "PAYMONGO_WEBHOOK_SECRET_KEY", "PAYMONGO_WEBHOOK_SECRET"], missingOrInvalid);
        RequireSecret(configuration, "Secrets:PayMongoSecretKey", ["Secrets__PayMongoSecretKey", "PAYMONGO_SECRET_KEY"], missingOrInvalid);
        RequireSecret(configuration, "Secrets:GoogleFormsSecret", ["Secrets__GoogleFormsSecret", "GOOGLE_FORMS_WEBHOOK_SECRET", "GOOGLE_FORMS_SECRET"], missingOrInvalid);
        RequireSecret(configuration, "Secrets:FieldEncryptionKeyBase64", ["Secrets__FieldEncryptionKeyBase64", "FIELD_ENCRYPTION_KEY_BASE64", "FIELD_ENCRYPTION_KEY"], missingOrInvalid);

        string? googleFormsSecret = ResolveSecretValue(configuration, "Secrets:GoogleFormsSecret", ["Secrets__GoogleFormsSecret", "GOOGLE_FORMS_WEBHOOK_SECRET", "GOOGLE_FORMS_SECRET"]);
        if (string.Equals(googleFormsSecret, CompromisedLegacyGoogleFormsSecret, StringComparison.Ordinal))
        {
            missingOrInvalid.Add(
                "Secrets:GoogleFormsSecret is using the compromised legacy key 'rge_gforms_live_sec_89f3a21'. Rotate immediately.");
        }

        string? encryptionKeyBase64 = ResolveSecretValue(configuration, "Secrets:FieldEncryptionKeyBase64", ["Secrets__FieldEncryptionKeyBase64", "FIELD_ENCRYPTION_KEY_BASE64", "FIELD_ENCRYPTION_KEY"]);
        if (!string.IsNullOrWhiteSpace(encryptionKeyBase64))
        {
            try
            {
                byte[] keyBytes = Convert.FromBase64String(encryptionKeyBase64);
                if (keyBytes.Length != 32)
                {
                    missingOrInvalid.Add("Secrets:FieldEncryptionKeyBase64 must decode to exactly 32 bytes (256 bits).");
                }
            }
            catch (FormatException)
            {
                missingOrInvalid.Add("Secrets:FieldEncryptionKeyBase64 is not valid Base64.");
            }
        }

        if (missingOrInvalid.Count > 0 && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Startup aborted due to secret validation failures: " + string.Join("; ", missingOrInvalid));
        }
    }

    private static string? ResolveSecretValue(IConfiguration config, string primaryKey, string[] fallbackKeys)
    {
        string? val = config[primaryKey];
        if (!string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        foreach (string fallback in fallbackKeys)
        {
            string? fallbackVal = config[fallback];
            if (!string.IsNullOrWhiteSpace(fallbackVal))
            {
                return fallbackVal;
            }
        }

        return null;
    }

    private static void RequireSecret(IConfiguration config, string primaryKey, string[] fallbackKeys, List<string> errors)
    {
        string? value = ResolveSecretValue(config, primaryKey, fallbackKeys);
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("TODO", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Missing or placeholder secret for '{primaryKey}'");
        }
    }
}

