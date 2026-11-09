using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;
using Rge.Receipts.Infrastructure.Mail;
using Rge.Receipts.Infrastructure.Pdf;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Empirical Adversarial Challenger Tests for Milestone 3:
/// Stress-tests loopback SMTP transmission, RFC 5322 MIME headers,
/// PDF attachment byte structure (%PDF-1.4), HTML body BIR compliance,
/// and rigorous XSS escaping.
/// </summary>
public sealed class SmtpLoopbackDeliveryChallengerTests
{
    private static OfficialReceipt CreateReceipt(
        string customerName = "Juan Dela Cruz",
        string customerEmail = "juan.delacruz@example.ph",
        long centavos = 112_00,
        string description = "Consulting Services")
    {
        var issuedAt = new DateTimeOffset(2026, 11, 8, 14, 30, 0, TimeSpan.Zero);
        var digest = new AuditDigest("sha256:v2:a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90");

        var result = OfficialReceipt.Issue(
            orderId: "ord_challenger_m3",
            orderNumber: "ORD-M3-CHALLENGER-001",
            paymentAttemptId: "pay_att_challenger_m3",
            sequenceNumber: 777,
            customerName: customerName,
            customerEmail: customerEmail,
            description: description,
            paymentMethod: "qrph",
            grossAmount: new Money(centavos, "PHP"),
            digest: digest,
            issuedAtUtc: issuedAt);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private static string DecodeQuotedPrintable(string mimeText)
    {
        // 1. Strip soft line breaks (=\r\n or =\n)
        string stripped = Regex.Replace(mimeText, @"=\r?\n", "");
        // 2. Decode common hex escapes
        stripped = stripped.Replace("=3D", "=", StringComparison.Ordinal)
                           .Replace("=0A", "\n", StringComparison.Ordinal)
                           .Replace("=0D", "\r", StringComparison.Ordinal)
                           .Replace("=20", " ", StringComparison.Ordinal);
        return stripped;
    }

    [Fact]
    public async Task LoopbackDelivery_AttachesOfficialReceiptPdf_WithValidPdfMagicBytesAndTrailer()
    {
        await using var server = new InProcessMockSmtpServer();

        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "test_password",
            EnableSsl = false,
            TimeoutMilliseconds = 10_000
        };

        var renderer = new QuestPdfReceiptRenderer();
        var mailer = new SmtpReceiptMailer(options, renderer);
        var receipt = CreateReceipt();
        byte[] renderedPdf = renderer.RenderOfficialReceiptPdf(receipt);

        var result = await mailer.SendReceiptEmailAsync(receipt, "customer@example.ph", renderedPdf);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.NotEmpty(server.ReceivedEmails);

        string rawMime = server.ReceivedEmails.First();

        // 1. MIME Header Inspection
        Assert.Contains("application/pdf", rawMime, StringComparison.OrdinalIgnoreCase);
        string expectedAttachmentName = $"OfficialReceipt-{receipt.ReceiptNumber.Value}.pdf";
        Assert.Contains(expectedAttachmentName, rawMime, StringComparison.Ordinal);

        // 2. Extract base64 attachment block from MIME
        var match = Regex.Match(
            rawMime,
            @"Content-Disposition:\s*attachment\r?\n\r?\n(?<b64>[A-Za-z0-9+/=\r\n]+?)\r?\n--",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        Assert.True(match.Success, "Failed to match base64 attachment block for " + expectedAttachmentName);

        string base64Data = Regex.Replace(match.Groups["b64"].Value, @"\s+", "");
        byte[] attachmentBytes = Convert.FromBase64String(base64Data);

        Assert.NotEmpty(attachmentBytes);
        Assert.True(attachmentBytes.Length > 1000, "PDF attachment is suspiciously small: " + attachmentBytes.Length + " bytes");

        // 3. Inspect PDF magic bytes and trailer
        string headerString = Encoding.ASCII.GetString(attachmentBytes[..8]);
        Assert.Equal("%PDF-1.4", headerString);

        string trailerString = Encoding.ASCII.GetString(attachmentBytes[^64..]);
        Assert.Contains("%%EOF", trailerString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoopbackDelivery_DeferredPdfGeneration_RendersValidPdfAttachment()
    {
        await using var server = new InProcessMockSmtpServer();

        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "test_password",
            EnableSsl = false,
            TimeoutMilliseconds = 10_000
        };

        var renderer = new QuestPdfReceiptRenderer();
        var mailer = new SmtpReceiptMailer(options, renderer);
        var receipt = CreateReceipt();

        // Pass empty byte array to trigger deferred internal rendering
        var result = await mailer.SendReceiptEmailAsync(receipt, "customer@example.ph", []);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.NotEmpty(server.ReceivedEmails);

        string rawMime = server.ReceivedEmails.First();
        string expectedAttachmentName = $"OfficialReceipt-{receipt.ReceiptNumber.Value}.pdf";
        Assert.Contains(expectedAttachmentName, rawMime, StringComparison.Ordinal);
        Assert.Contains("application/pdf", rawMime, StringComparison.OrdinalIgnoreCase);

        var match = Regex.Match(
            rawMime,
            @"Content-Disposition:\s*attachment\r?\n\r?\n(?<b64>[A-Za-z0-9+/=\r\n]+?)\r?\n--",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        Assert.True(match.Success, "Failed to match deferred base64 attachment block");
        byte[] bytes = Convert.FromBase64String(Regex.Replace(match.Groups["b64"].Value, @"\s+", ""));
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(bytes[..8]));
    }

    [Fact]
    public async Task LoopbackDelivery_HtmlBody_ContainsAllBirRequiredSections()
    {
        await using var server = new InProcessMockSmtpServer();

        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "test_password",
            EnableSsl = false,
            TimeoutMilliseconds = 10_000
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateReceipt();

        var result = await mailer.SendReceiptEmailAsync(receipt, "taxpayer@rge.ph", [1, 2, 3]);

        Assert.True(result.IsSuccess);
        string rawMime = server.ReceivedEmails.First();
        string decodedHtml = DecodeQuotedPrintable(rawMime);

        var vm = ReceiptLayout.ToViewModel(receipt);

        // 1. Header Brand Bar & BIR Registration
        Assert.Contains(vm.IssuerRegisteredName, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(vm.IssuerTin, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(vm.BirPermitNumber, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(vm.IssuerAddress, decodedHtml, StringComparison.Ordinal);

        // 2. Status Badge & Titles
        Assert.Contains("OFFICIAL RECEIPT", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("Issued", decodedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#dcfce7", decodedHtml, StringComparison.OrdinalIgnoreCase); // Status badge bg
        Assert.Contains("#15803d", decodedHtml, StringComparison.OrdinalIgnoreCase); // Status badge text

        // 3. Metadata
        Assert.Contains(receipt.ReceiptNumber.Value, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(receipt.CustomerName, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(receipt.CustomerEmail, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(receipt.OrderNumber, decodedHtml, StringComparison.Ordinal);

        // 4. VAT Breakdown Card
        Assert.Contains("VATable Sales:", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("12% Value-Added Tax:", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("VAT-Exempt Sales:", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("Zero-Rated Sales:", decodedHtml, StringComparison.Ordinal);
        Assert.Contains("TOTAL AMOUNT PAID:", decodedHtml, StringComparison.Ordinal);

        // 5. Cryptographic SHA-256 Digest Footer
        Assert.Contains("Audit Digest:", decodedHtml, StringComparison.Ordinal);
        Assert.Contains(receipt.Digest.Value, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlEncode(vm.LegalComplianceNote), decodedHtml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("\"><img src=x onerror=alert(1)>")]
    [InlineData("<b>Bold</b> & <i>Italic</i>")]
    [InlineData("Robert'); DROP TABLE Receipts;--")]
    public async Task LoopbackDelivery_HostileInputs_StrictlyHtmlEncodedWithoutExecution(string hostileInput)
    {
        ArgumentNullException.ThrowIfNull(hostileInput);

        await using var server = new InProcessMockSmtpServer();

        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "test_password",
            EnableSsl = false,
            TimeoutMilliseconds = 10_000
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateReceipt(customerName: hostileInput, description: hostileInput);

        var result = await mailer.SendReceiptEmailAsync(receipt, "victim@example.ph", [1, 2, 3]);

        Assert.True(result.IsSuccess);
        string rawMime = server.ReceivedEmails.First();
        string decodedHtml = DecodeQuotedPrintable(rawMime);

        // Raw unescaped dangerous tags must NEVER appear in the HTML
        if (hostileInput.Contains('<', StringComparison.Ordinal))
        {
            Assert.DoesNotContain("<script>", decodedHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", decodedHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<b>", decodedHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<i>", decodedHtml, StringComparison.OrdinalIgnoreCase);
        }

        // The exact WebUtility.HtmlEncode value MUST appear in the decoded HTML body
        string expectedEncoded = WebUtility.HtmlEncode(hostileInput);
        Assert.Contains(expectedEncoded, decodedHtml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", 587, "Receipts.InvalidSmtpHost")]
    [InlineData("   ", 587, "Receipts.InvalidSmtpHost")]
    [InlineData("127.0.0.1", 0, "Receipts.InvalidSmtpPort")]
    [InlineData("127.0.0.1", -1, "Receipts.InvalidSmtpPort")]
    [InlineData("127.0.0.1", 65536, "Receipts.InvalidSmtpPort")]
    [InlineData("127.0.0.1", 70000, "Receipts.InvalidSmtpPort")]
    public async Task LoopbackDelivery_InvalidConfiguration_FailsPreValidationFast(
        string host,
        int port,
        string expectedErrorCode)
    {
        var options = new SmtpReceiptOptions
        {
            Host = host,
            Port = port,
            SenderEmail = "valid@rge.ph",
            AppPassword = "password"
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateReceipt();

        var result = await mailer.SendReceiptEmailAsync(receipt, "user@example.ph", [1]);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedErrorCode, result.Error.Code);
    }
}
