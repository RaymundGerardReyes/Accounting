using Rge.BuildingBlocks.Infrastructure.Crypto;

namespace Rge.Integrations.Infrastructure;

public static class FormSecretVerifier
{
    public static bool VerifySecret(string? providedSecret, string? expectedSecret)
    {
        if (string.IsNullOrWhiteSpace(providedSecret) || string.IsNullOrWhiteSpace(expectedSecret))
        {
            return false;
        }

        string cleanProvided = providedSecret.Trim().Trim('"', '\'');
        string cleanExpected = expectedSecret.Trim().Trim('"', '\'');
        return ConstantTimeComparer.EqualsConstantTime(cleanProvided, cleanExpected);
    }
}

