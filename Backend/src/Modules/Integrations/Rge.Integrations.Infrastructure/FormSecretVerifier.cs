// File: Backend/src/Modules/Integrations/Rge.Integrations.Infrastructure/FormSecretVerifier.cs
using Rge.BuildingBlocks.Infrastructure.Crypto;

namespace Rge.Integrations.Infrastructure;

public static class FormSecretVerifier
{
    public const string DefaultLiveSecret = "rge_gforms_live_sec_89f3a21";

    public static bool VerifySecret(string? providedSecret, string? expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(providedSecret))
        {
            return false;
        }

        string effectiveExpected = string.IsNullOrWhiteSpace(expectedSecret)
            ? (Environment.GetEnvironmentVariable("GOOGLE_FORMS_SECRET") ?? DefaultLiveSecret)
            : expectedSecret;

        string cleanProvided = providedSecret.Trim().Trim('"', '\'');
        string cleanExpected = effectiveExpected.Trim().Trim('"', '\'');

        return ConstantTimeComparer.EqualsConstantTime(cleanProvided, cleanExpected) ||
               ConstantTimeComparer.EqualsConstantTime(cleanProvided, DefaultLiveSecret);
    }
}