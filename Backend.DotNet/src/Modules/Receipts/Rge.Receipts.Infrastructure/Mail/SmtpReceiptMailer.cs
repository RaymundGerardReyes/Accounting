using System.Net;
using System.Net.Mail;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;

namespace Rge.Receipts.Infrastructure.Mail;

public sealed class SmtpReceiptOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string SenderEmail { get; set; } = string.Empty;
    public string AppPassword { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public int TimeoutMilliseconds { get; set; } = 10_000;
}

/// <summary>
/// Dispatches XSS-safe BIR Official Receipts with attached vector PDF via Google Workspace SMTP (STARTTLS 587).
/// </summary>
public sealed class SmtpReceiptMailer : IReceiptMailer
{
    private readonly SmtpReceiptOptions _options;

    public SmtpReceiptMailer(SmtpReceiptOptions? options = null)
    {
        _options = options ?? new SmtpReceiptOptions();
    }

    public async Task<Result> SendReceiptEmailAsync(
        OfficialReceipt receipt,
        string recipientEmail,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(pdfBytes);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return Result.Failure(Error.Validation("Receipts.EmptyRecipient", "Recipient email cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(_options.SenderEmail) || string.IsNullOrWhiteSpace(_options.AppPassword))
        {
            return Result.Success();
        }

        string safeIssuer = WebUtility.HtmlEncode(BirRmc77_2024Policy.IssuerRegisteredName);
        string safeOrNumber = WebUtility.HtmlEncode(receipt.ReceiptNumber.Value);
        string safeAckNumber = WebUtility.HtmlEncode(receipt.Acknowledgement.AcknowledgementNumber);
        string safeOrderNumber = WebUtility.HtmlEncode(receipt.OrderNumber);
        string safeCustomerName = WebUtility.HtmlEncode(receipt.CustomerName);
        string safeAmount = WebUtility.HtmlEncode(receipt.VatBreakdown.GrossAmount.ToString());
        string safeDigest = WebUtility.HtmlEncode(receipt.Digest.Value);

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.SenderEmail, BirRmc77_2024Policy.IssuerRegisteredName),
                Subject = $"Official Receipt {receipt.ReceiptNumber.Value} - {BirRmc77_2024Policy.IssuerRegisteredName}",
                IsBodyHtml = true,
                Body = $"""
                    <div style="font-family: Arial, sans-serif; color: #1e293b;">
                      <h2>{safeIssuer}</h2>
                      <p>Dear {safeCustomerName},</p>
                      <p>Thank you for your payment. Attached is your BIR Official Receipt <strong>{safeOrNumber}</strong> and Treasury Acknowledgement <strong>{safeAckNumber}</strong>.</p>
                      <ul>
                        <li><strong>Order Number:</strong> {safeOrderNumber}</li>
                        <li><strong>Total Paid:</strong> {safeAmount}</li>
                        <li><strong>Digest:</strong> <code>{safeDigest}</code></li>
                      </ul>
                    </div>
                    """
            };

            message.To.Add(new MailAddress(recipientEmail.Trim()));

            using var pdfStream = new MemoryStream(pdfBytes, writable: false);
            message.Attachments.Add(new Attachment(pdfStream, $"{receipt.ReceiptNumber.Value}.pdf", "application/pdf"));

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                Timeout = _options.TimeoutMilliseconds,
                Credentials = new NetworkCredential(_options.SenderEmail, _options.AppPassword)
            };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(_options.TimeoutMilliseconds);

            await client.SendMailAsync(message, linkedCts.Token).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is SmtpException or OperationCanceledException or FormatException)
        {
            return Result.Failure(new Error("Receipts.SmtpSendFailed", $"SMTP dispatch failed: {ex.Message}"));
        }
    }
}
