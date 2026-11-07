#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA1849 // Call async methods when in an async method
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Contracts;
using Rge.Integrations.Infrastructure;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Mail;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Minimal in-process TCP SMTP mock server listening on ephemeral port 127.0.0.1:0.
/// Simulates RFC 5321 SMTP conversation (EHLO, AUTH LOGIN, MAIL FROM, RCPT TO, DATA, QUIT)
/// for local deterministic integration tests without external network dependencies.
/// </summary>
internal sealed class InProcessMockSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _serverTask;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public ConcurrentBag<string> ReceivedCommands { get; } = [];
    public ConcurrentBag<string> ReceivedEmails { get; } = [];
    public bool SimulateTimeout { get; set; }

    public InProcessMockSmtpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _serverTask = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose
        }
        catch (SocketException)
        {
            // Expected on listener stop
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII))
        await using (var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true })
        {
            if (SimulateTimeout)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return;
            }

            // Banner
            await writer.WriteLineAsync("220 127.0.0.1 InProcessMockSmtpServer ready".AsMemory(), ct);

            string? line;
            bool inDataMode = false;
            var dataBuffer = new StringBuilder();

            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                ReceivedCommands.Add(line);

                if (inDataMode)
                {
                    if (string.Equals(line, ".", StringComparison.Ordinal))
                    {
                        inDataMode = false;
                        ReceivedEmails.Add(dataBuffer.ToString());
                        dataBuffer.Clear();
                        await writer.WriteLineAsync("250 2.0.0 OK message accepted for delivery".AsMemory(), ct);
                    }
                    else
                    {
                        dataBuffer.AppendLine(line);
                    }
                    continue;
                }

                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-127.0.0.1".AsMemory(), ct);
                    await writer.WriteLineAsync("250-AUTH LOGIN".AsMemory(), ct);
                    await writer.WriteLineAsync("250-8BITMIME".AsMemory(), ct);
                    await writer.WriteLineAsync("250 OK".AsMemory(), ct);
                }
                else if (line.StartsWith("AUTH LOGIN", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = line.Length > 10 ? line[10..].Trim() : "";
                    if (string.IsNullOrEmpty(rest))
                    {
                        await writer.WriteLineAsync("334 VXNlcm5hbWU6".AsMemory(), ct); // Username: in base64
                        string? userB64 = await reader.ReadLineAsync(ct);
                        if (userB64 is not null) ReceivedCommands.Add(userB64);
                    }
                    else
                    {
                        ReceivedCommands.Add(rest);
                    }

                    await writer.WriteLineAsync("334 UGFzc3dvcmQ6".AsMemory(), ct); // Password: in base64
                    string? passB64 = await reader.ReadLineAsync(ct);
                    if (passB64 is not null) ReceivedCommands.Add(passB64);

                    await writer.WriteLineAsync("235 2.7.0 Authentication successful".AsMemory(), ct);
                }
                else if (line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("235 2.7.0 Authentication successful".AsMemory(), ct);
                }
                else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 2.1.0 Sender OK".AsMemory(), ct);
                }
                else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 2.1.5 Recipient OK".AsMemory(), ct);
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    inDataMode = true;
                    await writer.WriteLineAsync("354 Start mail input; end with <CRLF>.<CRLF>".AsMemory(), ct);
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 2.0.0 127.0.0.1 service closing transmission channel".AsMemory(), ct);
                    break;
                }
                else if (line.StartsWith("RSET", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 2.0.0 OK Reset".AsMemory(), ct);
                }
                else if (line.StartsWith("NOOP", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 2.0.0 OK".AsMemory(), ct);
                }
                else
                {
                    await writer.WriteLineAsync("500 5.5.1 Command unrecognized".AsMemory(), ct);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        _listener.Dispose();
        if (_serverTask is not null)
        {
            try
            {
                await _serverTask;
            }
            catch (Exception)
            {
                // Suppress shutdown exceptions
            }
        }
        _cts.Dispose();
    }
}

/// <summary>
/// SMTP Parity & Lifecycle Proof Suite:
/// 1. Verifies loopback TCP SMTP mail dispatch with real SmtpReceiptMailer.
/// 2. Proves SmtpClientFactory diagnostic probe parity with configured secrets.
/// 3. Tests timeout enforcement (10s default) when SMTP relay hangs or drops connections.
/// 4. Validates empty recipient and credential fallback boundaries.
/// </summary>
public sealed class SmtpParityAndLifecycleTests
{
    internal static OfficialReceipt CreateSampleReceipt(
        string orderId = "ord_smtp_001",
        string customerName = "Juan Dela Cruz")
    {
        var issuedAt = new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
        var digest = new AuditDigest("sha256:v2:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        var result = OfficialReceipt.Issue(
            orderId: orderId,
            orderNumber: $"ORD-{orderId}",
            paymentAttemptId: $"pay_{orderId}",
            sequenceNumber: 1,
            customerName: customerName,
            customerEmail: "customer@example.com",
            description: "Official Receipt Verification",
            paymentMethod: "qrph",
            grossAmount: new Money(100_00, "PHP"),
            digest: digest,
            issuedAtUtc: issuedAt);

        return result.Value;
    }

    [Fact]
    public async Task SmtpReceiptMailer_DispatchesMailToLoopbackServer_Successfully()
    {
        await using var server = new InProcessMockSmtpServer();

        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "test_app_password",
            EnableSsl = false, // Loopback plaintext test
            TimeoutMilliseconds = 5_000
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateSampleReceipt();
        byte[] dummyPdf = "%PDF-1.4 sample pdf content for proof test"u8.ToArray();

        var result = await mailer.SendReceiptEmailAsync(
            receipt: receipt,
            recipientEmail: "customer@example.com",
            pdfBytes: dummyPdf);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.NotEmpty(server.ReceivedCommands);
        Assert.NotEmpty(server.ReceivedEmails);

        string emailContent = server.ReceivedEmails.First();
        Assert.Contains("Official Receipt", emailContent, StringComparison.Ordinal);
        Assert.Contains(receipt.ReceiptNumber.Value, emailContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmtpReceiptMailer_WhenTimeoutExceeded_ReturnsCleanFailure_WithoutCrashing()
    {
        await using var server = new InProcessMockSmtpServer { SimulateTimeout = true };

        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "test_app_password",
            EnableSsl = false,
            TimeoutMilliseconds = 1_000 // Short 1s timeout to trip quickly
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateSampleReceipt();
        byte[] dummyPdf = "%PDF-1.4 sample"u8.ToArray();

        var result = await mailer.SendReceiptEmailAsync(
            receipt: receipt,
            recipientEmail: "timeout@example.com",
            pdfBytes: dummyPdf);

        Assert.False(result.IsSuccess);
        Assert.Equal("Receipts.SmtpSendFailed", result.Error.Code);
    }

    [Fact]
    public async Task SmtpReceiptMailer_EmptyRecipientEmail_ReturnsValidationFailure()
    {
        var options = new SmtpReceiptOptions
        {
            SenderEmail = "billing@rge.ph",
            AppPassword = "test_app_password"
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateSampleReceipt();

        var result = await mailer.SendReceiptEmailAsync(
            receipt: receipt,
            recipientEmail: "   ",
            pdfBytes: [1, 2, 3]);

        Assert.False(result.IsSuccess);
        Assert.Equal("Receipts.EmptyRecipient", result.Error.Code);
    }

    [Fact]
    public async Task SmtpReceiptMailer_UnconfiguredCredentials_BypassesSilentlyWithSuccess()
    {
        // Unconfigured mailer (e.g. dev or local mode)
        var options = new SmtpReceiptOptions
        {
            SenderEmail = "",
            AppPassword = ""
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateSampleReceipt();

        var result = await mailer.SendReceiptEmailAsync(
            receipt: receipt,
            recipientEmail: "customer@example.com",
            pdfBytes: [1, 2, 3]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SmtpClientFactory_DiagnosticProbe_ExecutesAndMeasuresBoundedLatency()
    {
        var secretStore = new SecretStoreWriter();
        var factory = new SmtpClientFactory(secretStore);

        var request = new EmailTestRequest(
            Host: "smtp.example.com",
            Port: 587,
            SendTestEmail: true,
            Recipient: "audit@example.com");

        var response = await factory.TestSmtpAsync(request);

        Assert.True(response.Success);
        Assert.Equal("connected", response.Probe.OperationalState);
        Assert.True(response.Probe.LatencyMs >= 1);
        Assert.NotNull(response.EmailDispatch);
        Assert.True(response.EmailDispatch.Dispatched);
        Assert.Equal("audit@example.com", response.EmailDispatch.Recipient);
    }
}
