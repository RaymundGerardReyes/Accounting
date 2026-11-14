using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;
using Rge.Receipts.Infrastructure.Mail.EmailTemplates;
using Rge.Receipts.Infrastructure.Pdf;

namespace Rge.Receipts.Infrastructure.Mail;

public sealed class SmtpReceiptOptions
{
    public string Host { get; set; } = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";
    public int Port { get; set; } = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out int p) ? p : 587;
    public string SenderEmail { get; set; } = Environment.GetEnvironmentVariable("SMTP_USER") ?? Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL") ?? string.Empty;
    public string AppPassword { get; set; } = Environment.GetEnvironmentVariable("SMTP_APP_PASSWORD") ?? string.Empty;
    public bool EnableSsl { get; set; } = !string.Equals(Environment.GetEnvironmentVariable("SMTP_USE_TLS"), "false", StringComparison.OrdinalIgnoreCase);
    public int TimeoutMilliseconds { get; set; } = int.TryParse(Environment.GetEnvironmentVariable("SMTP_TIMEOUT_MS"), out int t) ? t : 10_000;
}

/// <summary>
/// Dispatches XSS-safe BIR Official Receipts with attached vector PDF via Google Workspace SMTP (STARTTLS 587).
/// Pre-validates SMTP configuration prior to socket connection and awaits server acceptance.
/// </summary>
public sealed class SmtpReceiptMailer : IReceiptMailer
{
    private readonly SmtpReceiptOptions _options;
    private readonly IPdfRenderer _pdfRenderer;

    public SmtpReceiptMailer(SmtpReceiptOptions? options = null, IPdfRenderer? pdfRenderer = null)
        : this(options, pdfRenderer, null)
    {
    }

    public SmtpReceiptMailer(SmtpReceiptOptions? options, IConfiguration? configuration)
        : this(options, null, configuration)
    {
    }

    public SmtpReceiptMailer(SmtpReceiptOptions? options, IPdfRenderer? pdfRenderer, IConfiguration? configuration)
    {
        _options = options ?? new SmtpReceiptOptions();
        _pdfRenderer = pdfRenderer ?? new QuestPdfReceiptRenderer();

        if (configuration is not null)
        {
            if (string.IsNullOrWhiteSpace(_options.SenderEmail))
            {
                _options.SenderEmail = configuration["Smtp:SenderEmail"]
                    ?? configuration["SMTP_USER"]
                    ?? configuration["SMTP_SENDER_EMAIL"]
                    ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_options.AppPassword))
            {
                _options.AppPassword = configuration["Smtp:AppPassword"]
                    ?? configuration["SMTP_APP_PASSWORD"]
                    ?? string.Empty;
            }
        }
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

        if (!MailAddress.TryCreate(recipientEmail.Trim(), out MailAddress? parsedRecipient))
        {
            return Result.Failure(Error.Validation("Receipts.InvalidRecipient", "Recipient email address is invalid."));
        }

        // Configuration Pre-Validation
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            return Result.Failure(Error.Validation("Receipts.InvalidSmtpHost", "SMTP host cannot be null or empty."));
        }

        if (_options.Port is < 1 or > 65535)
        {
            return Result.Failure(Error.Validation("Receipts.InvalidSmtpPort", "SMTP port must be within valid TCP range (1..65535)."));
        }

        if (!string.IsNullOrWhiteSpace(_options.SenderEmail) && !MailAddress.TryCreate(_options.SenderEmail, out _))
        {
            return Result.Failure(Error.Validation("Receipts.InvalidSenderEmail", "SMTP sender email is not a valid email address."));
        }

        // Dev/Test Unconfigured Bypass (Mandatory Invariant)
        if (string.IsNullOrWhiteSpace(_options.SenderEmail) || string.IsNullOrWhiteSpace(_options.AppPassword))
        {
            return Result.Success();
        }

        try
        {
            var vm = ReceiptLayout.ToViewModel(receipt);
            string htmlBody = ReceiptEmailHtmlBuilder.BuildReceiptHtml(vm);

            using var message = new MailMessage
            {
                From = new MailAddress(_options.SenderEmail, BirRmc77_2024Policy.IssuerRegisteredName),
                Subject = $"Official Receipt {receipt.ReceiptNumber.Value} - {BirRmc77_2024Policy.IssuerRegisteredName}",
                IsBodyHtml = true,
                Body = htmlBody,
                BodyEncoding = Encoding.UTF8,
                BodyTransferEncoding = TransferEncoding.QuotedPrintable
            };

            message.To.Add(parsedRecipient);

            using var pdfStream = new MemoryStream(pdfBytes.Length > 0 ? pdfBytes : _pdfRenderer.RenderOfficialReceiptPdf(receipt), writable: false);
            string attachmentFilename = $"OfficialReceipt-{receipt.ReceiptNumber.Value}.pdf";
            var attachment = new Attachment(pdfStream, attachmentFilename, "application/pdf");
            message.Attachments.Add(attachment);

            string cleanPassword = _options.AppPassword.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                Timeout = _options.TimeoutMilliseconds,
                Credentials = new NetworkCredential(_options.SenderEmail.Trim(), cleanPassword)
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
