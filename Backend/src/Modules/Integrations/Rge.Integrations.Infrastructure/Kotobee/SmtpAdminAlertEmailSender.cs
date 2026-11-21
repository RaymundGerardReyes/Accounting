using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

[SuppressMessage("Performance", "CA1873:Avoid unnecessary evaluation")]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
public sealed class SmtpAdminAlertEmailSender : IAdminAlertEmailSender
{
    private readonly IOptionsMonitor<KotobeeOptions> _optionsMonitor;
    private readonly ILogger<SmtpAdminAlertEmailSender> _logger;
    private static readonly ConcurrentDictionary<string, DateTimeOffset> AlertCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(5);

    public SmtpAdminAlertEmailSender(
        IOptionsMonitor<KotobeeOptions> optionsMonitor,
        ILogger<SmtpAdminAlertEmailSender>? logger = null)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? NullLogger<SmtpAdminAlertEmailSender>.Instance;
    }

    public async Task SendSecurityAlertAsync(string subject, string bodyHtml, CancellationToken ct = default)
    {
        var opts = _optionsMonitor.CurrentValue;

        // Anti-flood 5-minute cooldown per subject
        var now = DateTimeOffset.UtcNow;
        if (AlertCooldowns.TryGetValue(subject, out var lastSent) && now - lastSent < CooldownDuration)
        {
            _logger.LogInformation("Security alert '{Subject}' throttled by 5-minute cooldown.", subject);
            return;
        }

        AlertCooldowns[subject] = now;

        var sanitizedSubject = HtmlEncoder.Default.Encode(subject);
        var recipient = opts.AdminAlertEmail;
        var sender = opts.AdminAlertSender;

        if (string.IsNullOrWhiteSpace(opts.SmtpHost))
        {
            _logger.LogWarning("SMTP host unconfigured. Security alert logged only: {Subject} -> {Recipient}", sanitizedSubject, recipient);
            return;
        }

        try
        {
            using var client = new SmtpClient(opts.SmtpHost, opts.SmtpPort)
            {
                EnableSsl = opts.SmtpEnableSsl
            };

            if (!string.IsNullOrWhiteSpace(opts.SmtpUsername) && !string.IsNullOrWhiteSpace(opts.SmtpPassword))
            {
                client.Credentials = new NetworkCredential(opts.SmtpUsername, opts.SmtpPassword);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(sender),
                Subject = $"[SECURITY ALERT] {sanitizedSubject}",
                Body = bodyHtml,
                IsBodyHtml = true
            };
            message.To.Add(recipient);

            await client.SendMailAsync(message, ct).ConfigureAwait(false);
            _logger.LogInformation("Security alert email sent to {Recipient} for '{Subject}'", recipient, sanitizedSubject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed sending security alert email to {Recipient}", recipient);
        }
    }
}
