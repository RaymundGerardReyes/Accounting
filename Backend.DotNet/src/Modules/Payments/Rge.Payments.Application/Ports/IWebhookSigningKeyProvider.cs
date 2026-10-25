namespace Rge.Payments.Application.Ports;

/// <summary>
/// Provides access to the webhook signing key for signature verification or testing simulation.
/// </summary>
public interface IWebhookSigningKeyProvider
{
    string GetWebhookSigningKey();
}
