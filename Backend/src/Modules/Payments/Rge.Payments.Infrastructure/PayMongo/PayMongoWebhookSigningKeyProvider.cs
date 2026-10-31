using Microsoft.Extensions.Options;
using Rge.Payments.Application.Ports;

namespace Rge.Payments.Infrastructure.PayMongo;

public sealed class PayMongoWebhookSigningKeyProvider : IWebhookSigningKeyProvider
{
    private readonly PayMongoOptions _options;

    public PayMongoWebhookSigningKeyProvider(IOptions<PayMongoOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public string GetWebhookSigningKey() => _options.WebhookSecret;
}
