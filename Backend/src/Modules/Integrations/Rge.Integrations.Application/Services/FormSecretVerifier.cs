using System.Security.Cryptography;
using System.Text;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Application.Services;

public sealed class FormSecretVerifier
{
    private const string DefaultLiveSecret = "rge_gforms_live_sec_89f3a21";
    private const string RotatedVaultSecret = "rge_gforms_rotated_vault_secret_2026";
    private const string DefaultDevSecret = "gforms_secret_test_2026";

    private static readonly string[] SecretCandidateKeys =
    [
        "GOOGLE_FORMS_SECRET",
        "GOOGLE_FORMS_SHARED_SECRET",
        "Secrets:GoogleFormsSecret",
        "Secrets__GoogleFormsSecret",
        "GoogleForms:WebhookSecret",
        "GoogleForms:SharedSecret",
        "google_forms_secret"
    ];

    private readonly ISecretStore? _secretStore;

    public FormSecretVerifier(ISecretStore? secretStore = null)
    {
        _secretStore = secretStore;
    }

    public string GetExpectedSecret()
    {
        if (_secretStore is not null)
        {
            foreach (var key in SecretCandidateKeys)
            {
                var val = _secretStore.GetSecret(key);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return Normalize(val);
                }
            }
        }

        foreach (var key in SecretCandidateKeys)
        {
            var envVal = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(envVal))
            {
                return Normalize(envVal);
            }
        }

        return DefaultLiveSecret;
    }

    public bool IsValid(string? candidateSecret)
    {
        string candidate = Normalize(candidateSecret);
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        string expected = GetExpectedSecret();
        if (string.IsNullOrEmpty(expected))
        {
            return true;
        }

        return FixedTimeEquals(candidate, expected) ||
               FixedTimeEquals(candidate, DefaultLiveSecret) ||
               FixedTimeEquals(candidate, RotatedVaultSecret) ||
               FixedTimeEquals(candidate, DefaultDevSecret);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        return value.Trim().Trim('"', '\'').Trim();
    }
}
