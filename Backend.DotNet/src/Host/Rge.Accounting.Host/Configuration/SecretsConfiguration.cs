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

        RequireNonEmpty(configuration, "ConnectionStrings:AccountingDb", missingOrInvalid);
        RequireNonEmpty(configuration, "Secrets:PayMongoWebhookSecret", missingOrInvalid);
        RequireNonEmpty(configuration, "Secrets:PayMongoSecretKey", missingOrInvalid);
        RequireNonEmpty(configuration, "Secrets:GoogleFormsSecret", missingOrInvalid);
        RequireNonEmpty(configuration, "Secrets:FieldEncryptionKeyBase64", missingOrInvalid);

        string? googleFormsSecret = configuration["Secrets:GoogleFormsSecret"];
        if (string.Equals(googleFormsSecret, CompromisedLegacyGoogleFormsSecret, StringComparison.Ordinal))
        {
            missingOrInvalid.Add(
                "Secrets:GoogleFormsSecret is using the compromised legacy key 'rge_gforms_live_sec_89f3a21'. Rotate immediately.");
        }

        string? encryptionKeyBase64 = configuration["Secrets:FieldEncryptionKeyBase64"];
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

    private static void RequireNonEmpty(IConfiguration config, string key, List<string> errors)
    {
        string? value = config[key];
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("TODO", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Missing or placeholder secret for '{key}'");
        }
    }
}

