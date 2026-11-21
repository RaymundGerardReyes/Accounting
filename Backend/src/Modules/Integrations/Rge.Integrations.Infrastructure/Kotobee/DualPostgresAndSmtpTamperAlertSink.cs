using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

[SuppressMessage("Performance", "CA1873:Avoid unnecessary evaluation")]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
public sealed class DualPostgresAndSmtpTamperAlertSink : ITamperAlertSink
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly IAdminAlertEmailSender _alertEmailSender;
    private readonly ILogger<DualPostgresAndSmtpTamperAlertSink> _logger;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> AlertCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(5);

    public DualPostgresAndSmtpTamperAlertSink(
        IAdminAlertEmailSender alertEmailSender,
        NpgsqlDataSource? dataSource = null,
        ILogger<DualPostgresAndSmtpTamperAlertSink>? logger = null)
    {
        _alertEmailSender = alertEmailSender ?? throw new ArgumentNullException(nameof(alertEmailSender));
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<DualPostgresAndSmtpTamperAlertSink>.Instance;
    }

    public async Task RecordTamperAttemptAsync(TamperAlertEvent alertEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alertEvent);

        _logger.LogWarning(
            "TAMPER DETECTED: Op={Operation}, Table={TableName}, Record={RecordId}, Actor={ActorId}, Reason={Reason}",
            alertEvent.Operation, alertEvent.TableName, alertEvent.RecordId, alertEvent.ActorId, alertEvent.AuditReason);

        // 1. Write to immutable SHA-256 hash-chained Postgres audit log
        if (_dataSource is not null)
        {
            try
            {
                const string sql = """
                    SELECT fn_kotobee_write_audit(
                        @table_name,
                        @operation,
                        @record_id,
                        @actor_id,
                        @audit_reason,
                        @correlation_id,
                        NULL,
                        @new_values::jsonb
                    );
                    """;

                await using var cmd = _dataSource.CreateCommand(sql);
                cmd.Parameters.AddWithValue("table_name", alertEvent.TableName);
                cmd.Parameters.AddWithValue("operation", alertEvent.Operation);
                cmd.Parameters.AddWithValue("record_id", alertEvent.RecordId);
                cmd.Parameters.AddWithValue("actor_id", alertEvent.ActorId);
                cmd.Parameters.AddWithValue("audit_reason", alertEvent.AuditReason);
                cmd.Parameters.AddWithValue("correlation_id", (object?)alertEvent.CorrelationId ?? DBNull.Value);

                var detailsJson = JsonSerializer.Serialize(new
                {
                    alertEvent.Details,
                    TimestampUtc = DateTimeOffset.UtcNow
                });
                cmd.Parameters.AddWithValue("new_values", detailsJson);

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed persisting tamper alert audit chain entry in Postgres.");
            }
        }

        // 2. Dispatch email alert with 5-minute cooldown
        var cooldownKey = $"{alertEvent.Operation}:{alertEvent.TableName}:{alertEvent.RecordId}";
        var now = DateTimeOffset.UtcNow;
        if (AlertCooldowns.TryGetValue(cooldownKey, out var lastSent) && now - lastSent < CooldownDuration)
        {
            _logger.LogInformation("Tamper alert email throttled by 5-minute cooldown: {Key}", cooldownKey);
            return;
        }
        AlertCooldowns[cooldownKey] = now;

        try
        {
            var subject = $"TAMPER ALERT: {alertEvent.Operation} on {alertEvent.TableName} ({alertEvent.RecordId})";
            var bodyHtml = $"""
                <h2>Security Alert: Tamper Attempt Intercepted</h2>
                <p><strong>Operation:</strong> {System.Net.WebUtility.HtmlEncode(alertEvent.Operation)}</p>
                <p><strong>Table:</strong> {System.Net.WebUtility.HtmlEncode(alertEvent.TableName)}</p>
                <p><strong>Record ID:</strong> {System.Net.WebUtility.HtmlEncode(alertEvent.RecordId)}</p>
                <p><strong>Actor ID:</strong> {System.Net.WebUtility.HtmlEncode(alertEvent.ActorId)}</p>
                <p><strong>Reason:</strong> {System.Net.WebUtility.HtmlEncode(alertEvent.AuditReason)}</p>
                <p><strong>Correlation ID:</strong> {System.Net.WebUtility.HtmlEncode(alertEvent.CorrelationId ?? "N/A")}</p>
                <p><strong>Details:</strong></p>
                <pre>{System.Net.WebUtility.HtmlEncode(alertEvent.Details ?? "None")}</pre>
                <p><em>Enforced by Kotobee Pre-Execution Isolation Guard and PostgreSQL V0013 Trigger Protection.</em></p>
                """;

            await _alertEmailSender.SendSecurityAlertAsync(subject, bodyHtml, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed sending tamper alert email notification.");
        }
    }
}
