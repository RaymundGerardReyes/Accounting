using System.Text.Json.Serialization;

namespace Rge.Receipts.Api.Contracts;

/// <summary>
/// Dual-cased request contract for resending receipt email via SMTP (REC-004),
/// supporting snake_case, camelCase, and legacy property aliases.
/// </summary>
public sealed class ResendReceiptEmailRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("to_email")]
    public string? ToEmailSnake { get; set; }

    [JsonPropertyName("toEmail")]
    public string? ToEmailCamel { get; set; }

    [JsonPropertyName("recipient")]
    public string? Recipient { get; set; }

    [JsonPropertyName("recipient_email")]
    public string? RecipientEmailSnake { get; set; }

    [JsonPropertyName("recipientEmail")]
    public string? RecipientEmailCamel { get; set; }

    [JsonIgnore]
    public string? EffectiveEmail =>
        !string.IsNullOrWhiteSpace(ToEmailSnake) ? ToEmailSnake.Trim() :
        !string.IsNullOrWhiteSpace(ToEmailCamel) ? ToEmailCamel.Trim() :
        !string.IsNullOrWhiteSpace(Email) ? Email.Trim() :
        !string.IsNullOrWhiteSpace(RecipientEmailSnake) ? RecipientEmailSnake.Trim() :
        !string.IsNullOrWhiteSpace(RecipientEmailCamel) ? RecipientEmailCamel.Trim() :
        !string.IsNullOrWhiteSpace(Recipient) ? Recipient.Trim() :
        null;

    public ResendReceiptEmailRequest() { }

    public ResendReceiptEmailRequest(string? email)
    {
        Email = email;
        ToEmailSnake = email;
    }
}

