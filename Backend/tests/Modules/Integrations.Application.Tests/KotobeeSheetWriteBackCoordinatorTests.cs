using Microsoft.Extensions.Logging.Abstractions;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;
using Xunit;

namespace Rge.Integrations.Application.Tests;

public sealed class KotobeeSheetWriteBackCoordinatorTests
{
    private sealed class InMemoryEntitlementRepoAndTracker : IKotobeeEntitlementRepository, IKotobeeSheetWriteBackTracker
    {
        public List<KotobeeEntitlementRecord> Records { get; } = [];
        public List<(string OrderId, bool Success, string? Tab, int? Row, string? Error, AuditExecutionContext Audit)> TrackedResults { get; } = [];

        public Task UpsertPendingAsync(KotobeeEntitlementRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task MarkActiveAsync(string orderId, string bookId, string? uid, string bookAccess, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(string orderId, string bookId, string error, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task MarkRevokedAsync(string orderId, string bookId, CancellationToken ct = default)
        {
            var existing = Records.FirstOrDefault(r => r.OrderId == orderId && r.KotobeeBookId == bookId);
            if (existing is not null)
            {
                Records.Remove(existing);
                Records.Add(existing with { Status = "Revoked", BookAccessState = "none" });
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KotobeeEntitlementRecord>> GetByOrderIdAsync(string orderId, CancellationToken ct = default)
        {
            var list = Records.Where(r => string.Equals(r.OrderId, orderId, StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult<IReadOnlyList<KotobeeEntitlementRecord>>(list);
        }

        public Task RecordSheetWriteBackResultAsync(
            string orderId,
            bool success,
            string? updatedTabName,
            int? updatedRowNumber,
            string? errorMessage,
            AuditExecutionContext auditContext,
            CancellationToken ct = default)
        {
            TrackedResults.Add((orderId, success, updatedTabName, updatedRowNumber, errorMessage, auditContext));
            return Task.CompletedTask;
        }
    }

    private sealed class StubSheetWriter : IKotobeeSheetStatusWriter
    {
        public KotobeeSheetStatusWriteBackRequest? LastRequest { get; private set; }
        public KotobeeSheetWriteBackResult Response { get; set; } = new(true, "Sheet1", 10, null);

        public Task<KotobeeSheetWriteBackResult> WriteActivationStatusAsync(
            KotobeeSheetStatusWriteBackRequest request,
            CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    [Fact]
    public async Task SyncPaidFulfillmentOutcomeAsync_WhenAllActive_WritesActivatedStatusAndAudits()
    {
        var repo = new InMemoryEntitlementRepoAndTracker();
        repo.Records.Add(new KotobeeEntitlementRecord(
            Guid.NewGuid(), "ORD-1", "SKU-1", "buyer@example.com", "uid1", "library", "lib1", "101",
            "Active", "full", 1, null, null, DateTimeOffset.UtcNow));
        repo.Records.Add(new KotobeeEntitlementRecord(
            Guid.NewGuid(), "ORD-1", "SKU-2", "buyer@example.com", "uid1", "library", "lib1", "102",
            "Active", "full", 1, null, null, DateTimeOffset.UtcNow));

        var writer = new StubSheetWriter();
        var coordinator = new KotobeeSheetWriteBackCoordinator(repo, writer, repo, NullLogger<KotobeeSheetWriteBackCoordinator>.Instance);

        var audit = new AuditExecutionContext("kotobee:worker", "Paid order fulfillment completed", "ORD-1");
        var result = await coordinator.SyncPaidFulfillmentOutcomeAsync("ORD-1", "buyer@example.com", "SKU-1, SKU-2", audit);

        Assert.True(result.Success);
        Assert.NotNull(writer.LastRequest);
        Assert.Equal("ACTIVATED", writer.LastRequest.KotobeeStatus);
        Assert.Equal("PAID", writer.LastRequest.PaymentStatus);
        Assert.Contains("101", writer.LastRequest.ActivatedBookIds);
        Assert.Contains("102", writer.LastRequest.ActivatedBookIds);

        Assert.Single(repo.TrackedResults);
        Assert.Equal("ORD-1", repo.TrackedResults[0].OrderId);
        Assert.True(repo.TrackedResults[0].Success);
        Assert.Equal("kotobee:worker", repo.TrackedResults[0].Audit.ActorId);
    }

    [Fact]
    public async Task SyncPaidFulfillmentOutcomeAsync_WhenPartialFailed_WritesPartialFailedStatus()
    {
        var repo = new InMemoryEntitlementRepoAndTracker();
        repo.Records.Add(new KotobeeEntitlementRecord(
            Guid.NewGuid(), "ORD-2", "SKU-1", "buyer@example.com", "uid1", "library", "lib1", "101",
            "Active", "full", 1, null, null, DateTimeOffset.UtcNow));
        repo.Records.Add(new KotobeeEntitlementRecord(
            Guid.NewGuid(), "ORD-2", "SKU-2", "buyer@example.com", "uid1", "library", "lib1", "102",
            "Failed", null, 3, "Kotobee API 500 error", null, null));

        var writer = new StubSheetWriter();
        var coordinator = new KotobeeSheetWriteBackCoordinator(repo, writer, repo, NullLogger<KotobeeSheetWriteBackCoordinator>.Instance);

        var audit = new AuditExecutionContext("kotobee:worker", "Partial fulfillment status", "ORD-2");
        var result = await coordinator.SyncPaidFulfillmentOutcomeAsync("ORD-2", "buyer@example.com", null, audit);

        Assert.True(result.Success);
        Assert.NotNull(writer.LastRequest);
        Assert.Equal("PARTIAL_FAILED", writer.LastRequest.KotobeeStatus);
        Assert.Contains("101", writer.LastRequest.ActivatedBookIds);
        Assert.Contains("102: Kotobee API 500 error", writer.LastRequest.Notes);
    }

    [Fact]
    public async Task SyncPaidFulfillmentOutcomeAsync_WhenAllFailed_WritesFailedStatus()
    {
        var repo = new InMemoryEntitlementRepoAndTracker();
        repo.Records.Add(new KotobeeEntitlementRecord(
            Guid.NewGuid(), "ORD-3", "SKU-1", "buyer@example.com", null, "library", "lib1", "101",
            "Failed", null, 3, "Rate limit exceeded", null, null));

        var writer = new StubSheetWriter();
        var coordinator = new KotobeeSheetWriteBackCoordinator(repo, writer, repo, NullLogger<KotobeeSheetWriteBackCoordinator>.Instance);

        var audit = new AuditExecutionContext("kotobee:worker", "Failed fulfillment status", "ORD-3");
        var result = await coordinator.SyncPaidFulfillmentOutcomeAsync("ORD-3", "buyer@example.com", null, audit);

        Assert.True(result.Success);
        Assert.NotNull(writer.LastRequest);
        Assert.Equal("FAILED", writer.LastRequest.KotobeeStatus);
        Assert.Empty(writer.LastRequest.ActivatedBookIds);
    }

    [Fact]
    public async Task SyncPaidFulfillmentOutcomeAsync_WhenNoRecords_WritesNoEbookItemsStatus()
    {
        var repo = new InMemoryEntitlementRepoAndTracker();
        var writer = new StubSheetWriter();
        var coordinator = new KotobeeSheetWriteBackCoordinator(repo, writer, repo, NullLogger<KotobeeSheetWriteBackCoordinator>.Instance);

        var audit = new AuditExecutionContext("kotobee:worker", "Order has no ebook items", "ORD-4");
        var result = await coordinator.SyncPaidFulfillmentOutcomeAsync("ORD-4", "buyer@example.com", null, audit);

        Assert.True(result.Success);
        Assert.NotNull(writer.LastRequest);
        Assert.Equal("NO_EBOOK_ITEMS", writer.LastRequest.KotobeeStatus);
        Assert.Empty(repo.TrackedResults); // No records to update in DB
    }

    [Fact]
    public async Task SyncFailedPaymentOutcomeAsync_WritesSkippedUnpaidWithoutCallingKotobeeOrRepo()
    {
        var repo = new InMemoryEntitlementRepoAndTracker();
        var writer = new StubSheetWriter();
        var coordinator = new KotobeeSheetWriteBackCoordinator(repo, writer, repo, NullLogger<KotobeeSheetWriteBackCoordinator>.Instance);

        var result = await coordinator.SyncFailedPaymentOutcomeAsync(
            "ORD-UNPAID",
            "buyer@example.com",
            paymentStatus: "FAILED",
            failureReason: "Card declined by issuing bank"
        );

        Assert.True(result.Success);
        Assert.NotNull(writer.LastRequest);
        Assert.Equal("ORD-UNPAID", writer.LastRequest.OrderId);
        Assert.Equal("FAILED", writer.LastRequest.PaymentStatus);
        Assert.Equal("SKIPPED_UNPAID", writer.LastRequest.KotobeeStatus);
        Assert.Contains("Card declined by issuing bank", writer.LastRequest.Notes);
        Assert.Empty(repo.Records);
        Assert.Empty(repo.TrackedResults);
    }
}
