namespace Rge.Integrations.Infrastructure;

public sealed class FormSecretVerifier
{
    private readonly Rge.Integrations.Application.Services.FormSecretVerifier _inner;

    public FormSecretVerifier(
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null,
        Rge.Integrations.Application.Ports.ISecretStore? secretStore = null)
    {
        _inner = new Rge.Integrations.Application.Services.FormSecretVerifier(secretStore);
    }

    public string GetExpectedSecret() => _inner.GetExpectedSecret();

    public bool IsValid(string? candidateSecret) => _inner.IsValid(candidateSecret);
}
