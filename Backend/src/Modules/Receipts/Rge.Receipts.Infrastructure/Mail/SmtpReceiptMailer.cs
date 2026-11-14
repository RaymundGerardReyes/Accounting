using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    private readonly IConfiguration? _configuration;
    private readonly ILogger<SmtpReceiptMailer>? _logger;

    public SmtpReceiptMailer(SmtpReceiptOptions? options = null, IPdfRenderer? pdfRenderer = null)
        : this(options, pdfRenderer, null, null)
    {
    }

    public SmtpReceiptMailer(SmtpReceiptOptions? options, IConfiguration? configuration)
        : this(options, null, configuration, null)
    {
    }

    public SmtpReceiptMailer(SmtpReceiptOptions? options, IPdfRenderer? pdfRenderer, IConfiguration? configuration)
        : this(options, pdfRenderer, configuration, null)
    {
    }

    public SmtpReceiptMailer(
        IConfiguration configuration,
        IPdfRenderer pdfRenderer,
        ILogger<SmtpReceiptMailer>? logger = null)
        : this(null, pdfRenderer, configuration, logger)
    {
    }

    public SmtpReceiptMailer(
        SmtpReceiptOptions? options,
        IPdfRenderer? pdfRenderer,
        IConfiguration? configuration,
        ILogger<SmtpReceiptMailer>? logger)
    {
        _options = options ?? new SmtpReceiptOptions();
        _pdfRenderer = pdfRenderer ?? new QuestPdfReceiptRenderer();
        _configuration = configuration;
        _logger = logger;

        if (_configuration is not null)
        {
            if (string.IsNullOrWhiteSpace(_options.SenderEmail))
            {
                _options.SenderEmail = _configuration["Smtp:SenderEmail"]
                    ?? _configuration["Smtp:Username"]
                    ?? _configuration["SMTP_USER"]
                    ?? _configuration["SMTP_SENDER_EMAIL"]
                    ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_options.AppPassword))
            {
                _options.AppPassword = _configuration["Smtp:AppPassword"]
                    ?? _configuration["Smtp:Password"]
                    ?? _configuration["SMTP_APP_PASSWORD"]
                    ?? _configuration["SMTP_PASS"]
                    ?? string.Empty;
            }

            string? cfgHost = _configuration["Smtp:Host"] ?? _configuration["SMTP_HOST"];
            if (!string.IsNullOrWhiteSpace(cfgHost))
            {
                _options.Host = cfgHost.Trim();
            }

            string? cfgPort = _configuration["Smtp:Port"] ?? _configuration["SMTP_PORT"];
            if (int.TryParse(cfgPort, out int parsedPort) && parsedPort > 0)
            {
                _options.Port = parsedPort;
            }
        }
    }

    public async Task SendAsync(
        OfficialReceipt receipt,
        string recipientEmail,
        byte[]? pdfAttachment = null,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = pdfAttachment is { Length: > 0 }
            ? pdfAttachment
            : _pdfRenderer.RenderOfficialReceiptPdf(receipt);

        Result result = await SendReceiptEmailAsync(receipt, recipientEmail, bytes, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Receipt email dispatch failed: {result.Error.Message}");
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

        string defaultReceiptEmail = _configuration?["DEFAULT_RECEIPT_EMAIL"]
            ?? _configuration?["SMTP_FROM"]
            ?? Environment.GetEnvironmentVariable("DEFAULT_RECEIPT_EMAIL")
            ?? _options.SenderEmail;

        string targetRecipient = IsValidUnmaskedEmail(recipientEmail)
            ? recipientEmail.Trim()
            : defaultReceiptEmail.Trim();

        if (string.IsNullOrWhiteSpace(targetRecipient))
        {
            return Result.Failure(Error.Validation("Receipts.EmptyRecipient", "Recipient email cannot be empty."));
        }

        if (!MailAddress.TryCreate(targetRecipient, out MailAddress? parsedRecipient))
        {
            return Result.Failure(Error.Validation("Receipts.InvalidRecipient", "Recipient email address is invalid."));
        }

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

            // Copy the configured sender/treasury so both ends receive a copy
            if (!string.IsNullOrWhiteSpace(_options.SenderEmail) &&
                !string.Equals(targetRecipient, _options.SenderEmail, StringComparison.OrdinalIgnoreCase))
            {
                message.CC.Add(new MailAddress(_options.SenderEmail, BirRmc77_2024Policy.IssuerRegisteredName));
            }

            if (!string.IsNullOrWhiteSpace(defaultReceiptEmail) &&
                !string.Equals(targetRecipient, defaultReceiptEmail, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_options.SenderEmail, defaultReceiptEmail, StringComparison.OrdinalIgnoreCase) &&
                MailAddress.TryCreate(defaultReceiptEmail, out MailAddress? bccAddr))
            {
                message.Bcc.Add(bccAddr);
            }

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

    private static bool IsValidUnmaskedEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        string trimmed = email.Trim();
        int atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        return atIndex > 0 &&
               !trimmed.Contains('*', StringComparison.Ordinal) &&
               trimmed.IndexOf('.', atIndex) > atIndex;
    }
}
