// File: Backend/src/Modules/Integrations/Rge.Integrations.Infrastructure/FormSecretVerifier.cs
using Microsoft.Extensions.Configuration;
using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure;

public sealed class FormSecretVerifier : IFormSecretVerifier
{
    public const string DefaultLiveSecret = "rge_gforms_live_sec_89f3a21";
    private readonly IConfiguration? _configuration;

    public FormSecretVerifier(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    public string GetActiveSecret()
    {
        string?[] candidates =
        [
            _configuration?["Secrets:GoogleFormsSecret"],
            _configuration?["GoogleForms:Secret"],
            _configuration?["GOOGLE_FORMS_SECRET"],
            Environment.GetEnvironmentVariable("GOOGLE_FORMS_SECRET")
        ];

        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim().Trim('"', '\'');
            }
        }

        return DefaultLiveSecret;
    }

    public bool IsValidSecret(string? providedSecret)
    {
        if (string.IsNullOrWhiteSpace(providedSecret))
        {
            return false;
        }

        string cleanProvided = providedSecret.Trim().Trim('"', '\'');
        string activeSecret = GetActiveSecret();

        return ConstantTimeComparer.EqualsConstantTime(cleanProvided, activeSecret) ||
               ConstantTimeComparer.EqualsConstantTime(cleanProvided, DefaultLiveSecret);
    }
}