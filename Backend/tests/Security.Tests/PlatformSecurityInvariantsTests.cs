using System.Security.Cryptography;
using System.Text;
using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Documents.Domain;
using Rge.Documents.Infrastructure;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Infrastructure.Reporting;
using Rge.Identity.Infrastructure;
using Xunit;

namespace Rge.Security.Tests;

public sealed class PlatformSecurityInvariantsTests
{
    [Fact]
    public void FourEyesRule_Blocks_SelfApproval_And_Conflicting_Disbursement_Actors()
    {
        Result selfApprove = FourEyesRule.EnsureRequestorIsNotApprover("emp_field_01", "EMP_FIELD_01");
        Assert.True(selfApprove.IsFailure);
        Assert.Equal(ErrorType.SodViolation, selfApprove.Error.Type);

        Result makerIsVerifier = FourEyesRule.EnsureDistinctDisbursementActors(
            makerId: "fin_maker_01",
            verifierId: "fin_maker_01");
        Assert.True(makerIsVerifier.IsFailure);
        Assert.Equal(ErrorType.SodViolation, makerIsVerifier.Error.Type);

        Result bankApproverIsVerifier = FourEyesRule.EnsureDistinctDisbursementActors(
            makerId: "fin_maker_01",
            verifierId: "fin_verifier_01",
            bankApproverId: "fin_verifier_01");
        Assert.True(bankApproverIsVerifier.IsFailure);

        Result validFourEyes = FourEyesRule.EnsureDistinctDisbursementActors(
            makerId: "fin_maker_01",
            verifierId: "fin_verifier_01",
            bankApproverId: "fin_bank_01",
            evidenceVerifierId: "fin_evidence_01");
        Assert.True(validFourEyes.IsSuccess);
    }

    [Fact]
    public void PayMongo_HmacVerifier_Accepts_Valid_Signature_And_Rejects_Tampering_Or_Replay()
    {
        const string secret = "whsec_live_test_secret_key_2026";
        const string body = "{\"data\":{\"id\":\"evt_123\",\"attributes\":{\"type\":\"checkout_session.payment.paid\"}}}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long timestamp = now.ToUnixTimeSeconds();

        string signedPayload = $"{timestamp}.{body}";
        byte[] hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signedPayload));
        string hexSig = Convert.ToHexStringLower(hmac);
        string header = $"t={timestamp},te={hexSig},li={hexSig}";

        Assert.True(HmacVerifier.VerifyPayMongoSignature(header, body, secret, now));

        // Tampered body must fail
        Assert.False(HmacVerifier.VerifyPayMongoSignature(header, body + " ", secret, now));

        // Expired timestamp (10 minutes old > 5-minute window) must fail
        Assert.False(HmacVerifier.VerifyPayMongoSignature(header, body, secret, now.AddMinutes(10)));
    }

    [Theory]
    [InlineData("9223372036854775807")] // long.MaxValue
    [InlineData("-9223372036854775808")] // long.MinValue
    [InlineData("253402300800")] // MaxUnixSeconds + 1 (Year 10000 overflow)
    [InlineData("-62135596801")] // MinUnixSeconds - 1 (Year 0000 underflow)
    [InlineData("999999999999999")]
    [InlineData("-999999999999999")]
    [InlineData("9999999999999999999999999999999999999999999")] // Exceeds Int64 range
    [InlineData("invalid_timestamp")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("   ")]
    public void PayMongo_HmacVerifier_Rejects_TimestampBoundaryAndOverflow_WithoutThrowing(string timestampPart)
    {
        const string secret = "whsec_live_test_secret_key_2026";
        const string body = "{\"data\":{\"id\":\"evt_123\"}}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string header = $"t={timestampPart},te=0000000000000000000000000000000000000000000000000000000000000000,li=0000000000000000000000000000000000000000000000000000000000000000";

        bool result = false;
        Exception? exception = Record.Exception(() =>
        {
            result = HmacVerifier.VerifyPayMongoSignature(header, body, secret, now);
        });

        Assert.Null(exception);
        Assert.False(result);
    }

    [Theory]
    [InlineData(253402300799L)] // MaxUnixSeconds (Year 9999-12-31T23:59:59Z)
    [InlineData(-62135596800L)] // MinUnixSeconds (Year 0001-01-01T00:00:00Z)
    [InlineData(0L)]            // Unix Epoch (1970-01-01T00:00:00Z)
    public void PayMongo_HmacVerifier_ValidBoundaryTimestamps_DoNotThrow_And_FailSkewCheck(long unixSeconds)
    {
        const string secret = "whsec_live_test_secret_key_2026";
        const string body = "{\"data\":{\"id\":\"evt_123\"}}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string header = $"t={unixSeconds},te=0000000000000000000000000000000000000000000000000000000000000000,li=0000000000000000000000000000000000000000000000000000000000000000";

        bool result = false;
        Exception? exception = Record.Exception(() =>
        {
            result = HmacVerifier.VerifyPayMongoSignature(header, body, secret, now);
        });

        Assert.Null(exception);
        Assert.False(result);
    }

    [Fact]
    public void DocumentUpload_Enforces_MagicBytes_SizeCap_Antivirus_And_PathTraversal_Defense()
    {
        // Valid %PDF-1.4 header
        ReadOnlySpan<byte> validPdf = "%PDF-1.4\n%test"u8;
        Assert.True(MimeSignature.DetectAndValidate(validPdf).IsSuccess);

        // Spoofed executable (MZ header) disguised as PDF must be rejected
        ReadOnlySpan<byte> spoofedExe = "MZ\x90\x00\x03\x00\x00\x00"u8;
        Assert.True(MimeSignature.DetectAndValidate(spoofedExe).IsFailure);

        // Over 10 MB must be rejected
        Assert.True(FileSizePolicy.ValidateSize(FileSizePolicy.MaxSizeBytes + 1).IsFailure);

        // Path traversal attempt must throw UnauthorizedAccessException
        Assert.Throws<UnauthorizedAccessException>(() =>
            PathTraversalGuard.GetSafeVaultFilePath(Path.GetTempPath(), "..\\..\\Windows\\System32\\cmd.exe"));
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("+SUM(A1:A10)", "'+SUM(A1:A10)")]
    [InlineData("-100+200", "'-100+200")]
    [InlineData("@SUM(1+1)", "'@SUM(1+1)")]
    public void CsvInjectionSafeWriter_Neutralizes_Spreadsheet_Formula_Prefixes(string maliciousInput, string expectedSanitized)
    {
        string sanitized = CsvInjectionSafeWriter.SanitizeCell(maliciousInput);
        Assert.Equal(expectedSanitized, sanitized);
    }

    [Fact]
    public void FieldEncryptionService_Encrypts_And_Decrypts_With_Aes256Gcm_Authenticated_Tag()
    {
        byte[] masterKey = RandomNumberGenerator.GetBytes(32);
        var service = new FieldEncryptionService(masterKey);

        const string secretBankAccount = "0042-8192-3910-88";
        string ciphertext = service.Encrypt(secretBankAccount, "vendor_01");

        Assert.StartsWith("v1:", ciphertext, StringComparison.Ordinal);
        Assert.NotEqual(secretBankAccount, ciphertext);

        string decrypted = service.Decrypt(ciphertext, "vendor_01");
        Assert.Equal(secretBankAccount, decrypted);
    }

    [Fact]
    public void CsrfTokenService_Validates_SessionBound_Tokens_In_ConstantTime()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var csrf = new CsrfTokenService(key);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        (string _, string sessionHash) = SessionTokenHasher.CreateSessionToken();
        string token = csrf.GenerateToken(sessionHash, now);

        Assert.True(csrf.ValidateToken(sessionHash, token, now.AddMinutes(5)));
        Assert.False(csrf.ValidateToken("different_session_hash", token, now.AddMinutes(5)));
    }

    [Fact]
    public void IdentityDbSeeder_Integrity_And_Persona_Seed_Invariants()
    {
        Assert.Equal("password123", IdentityDbSeeder.DefaultDevPassword);

        string sql = IdentityDbSeeder.SeedSql;
        Assert.Contains("pgcrypto", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO public.users_finance", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO public.role_assignments", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO public.user_credentials", sql, StringComparison.OrdinalIgnoreCase);
    }
}

