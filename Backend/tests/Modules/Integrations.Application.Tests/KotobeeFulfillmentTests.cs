using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;
using Xunit;

namespace Rge.Integrations.Application.Tests;

public sealed class KotobeeFulfillmentTests
{
    private sealed class StubPaidOrderVerifier : IPaidOrderVerifier
    {
        public bool IsPaid { get; set; } = true;

        public Task<bool> IsOrderConfirmedPaidAsync(string orderId, CancellationToken ct = default)
            => Task.FromResult(IsPaid);
    }

    private sealed class InMemoryEntitlementRepo : IKotobeeEntitlementRepository
    {
        public List<KotobeeEntitlementRecord> Records { get; } = [];

        public Task UpsertPendingAsync(KotobeeEntitlementRecord record, CancellationToken ct = default)
        {
            var existing = Records.FirstOrDefault(r => r.OrderId == record.OrderId && r.KotobeeBookId == record.KotobeeBookId);
            if (existing is not null)
            {
                Records.Remove(existing);
            }
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task MarkActiveAsync(string orderId, string bookId, string? uid, string bookAccess, CancellationToken ct = default)
        {
            var existing = Records.FirstOrDefault(r => r.OrderId == orderId && r.KotobeeBookId == bookId);
            if (existing is not null)
            {
                Records.Remove(existing);
                Records.Add(existing with
                {
                    Status = "Active",
                    KotobeeUid = uid ?? existing.KotobeeUid,
                    BookAccessState = bookAccess,
                    ActivatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(string orderId, string bookId, string error, CancellationToken ct = default)
        {
            var existing = Records.FirstOrDefault(r => r.OrderId == orderId && r.KotobeeBookId == bookId);
            if (existing is not null)
            {
                Records.Remove(existing);
                Records.Add(existing with { Status = "Failed", LastError = error });
            }
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
            => Task.FromResult<IReadOnlyList<KotobeeEntitlementRecord>>(Records.Where(r => r.OrderId == orderId).ToList());
    }

    private sealed class MockKotobeeApiClient : IKotobeeApiClient
    {
        public int GetUserCallCount { get; private set; }
        public int AddUserCallCount { get; private set; }
        public int EditUserCallCount { get; private set; }
        public bool SimulateExistingUser { get; set; }
        public string InitialBookAccess { get; set; } = "none";

        public Task<KotobeeUserPermissionResult> GetUserAsync(string? email, string? uid, string? libraryId, string? bookId, CancellationToken ct = default)
        {
            GetUserCallCount++;
            if (SimulateExistingUser || !string.IsNullOrWhiteSpace(uid))
            {
                // After AddUser is called, return full access
                string access = AddUserCallCount > 0 || EditUserCallCount > 0 ? "full" : InitialBookAccess;
                return Task.FromResult(new KotobeeUserPermissionResult(true, uid ?? "koto_uid_123", email ?? "buyer@example.com", access, "{}"));
            }

            return Task.FromResult(new KotobeeUserPermissionResult(false, null, email, "none", "{}"));
        }

        public Task<KotobeeMutationResult> AddUserOrGrantBookAsync(string? email, string? uid, string? fullName, string? libraryId, string bookId, DateOnly? accessExpirationDate, CancellationToken ct = default)
        {
            AddUserCallCount++;
            SimulateExistingUser = true;
            return Task.FromResult(new KotobeeMutationResult(true, uid ?? "koto_uid_123", null, "{}"));
        }

        public Task<KotobeeMutationResult> EditUserBookAccessAsync(string uid, string? libraryId, string bookId, bool active, DateOnly? accessExpirationDate, CancellationToken ct = default)
        {
            EditUserCallCount++;
            return Task.FromResult(new KotobeeMutationResult(true, uid, null, "{}"));
        }

        public Task<KotobeeMutationResult> RevokeBookAccessAsync(string? email, string? uid, string? libraryId, string bookId, CancellationToken ct = default)
            => Task.FromResult(new KotobeeMutationResult(true, uid, null, "{}"));

        public Task<IReadOnlyList<KotobeeBookSummary>> ListBooksAsync(string? libraryId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KotobeeBookSummary>>([
                new KotobeeBookSummary("bid_501", "Financial Accounting & Reporting Vol. 1"),
                new KotobeeBookSummary("bid_502", "Advanced Financial Accounting & Reporting")
            ]);
    }

    [Fact]
    public async Task FulfillPaidOrder_WhenOrderIsNotPaid_BlocksActivationEntirely()
    {
        var verifier = new StubPaidOrderVerifier { IsPaid = false };
        var client = new MockKotobeeApiClient();
        var repo = new InMemoryEntitlementRepo();
        var options = new KotobeeOptions
        {
            DefaultLibraryId = "100",
            ProductMappings = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SKU-BOOK-A"] = [new KotobeeProductBookMapping("bid_501")]
            }
        };
        var mappingResolver = new KotobeeCatalogMappingResolver(new OptionsMonitorStub(options));
        var service = new FulfillKotobeeOrderService(
            verifier,
            client,
            repo,
            mappingResolver,
            new OptionsMonitorStub(options),
            NullLogger<FulfillKotobeeOrderService>.Instance);

        var job = new KotobeeFulfillmentJob("ord_unpaid_123", "buyer@example.com", "Juan Dela Cruz", ["SKU-BOOK-A"]);
        await service.FulfillPaidOrderAsync(job);

        Assert.Equal(0, client.GetUserCallCount);
        Assert.Equal(0, client.AddUserCallCount);
        Assert.Empty(repo.Records);
    }

    [Fact]
    public async Task FulfillPaidOrder_WhenOrderIsConfirmedPaid_GrantsBookAndMarksActive()
    {
        var verifier = new StubPaidOrderVerifier { IsPaid = true };
        var client = new MockKotobeeApiClient();
        var repo = new InMemoryEntitlementRepo();
        var options = new KotobeeOptions
        {
            DefaultLibraryId = "100",
            ProductMappings = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SKU-BOOK-A"] = [new KotobeeProductBookMapping("bid_501", AccessDurationDays: 365)]
            }
        };
        var mappingResolver = new KotobeeCatalogMappingResolver(new OptionsMonitorStub(options));
        var service = new FulfillKotobeeOrderService(
            verifier,
            client,
            repo,
            mappingResolver,
            new OptionsMonitorStub(options),
            NullLogger<FulfillKotobeeOrderService>.Instance);

        var job = new KotobeeFulfillmentJob("ord_paid_456", "buyer@example.com", "Maria Clara", ["SKU-BOOK-A"]);
        await service.FulfillPaidOrderAsync(job);

        Assert.True(client.GetUserCallCount >= 2);
        Assert.Equal(1, client.AddUserCallCount);
        Assert.Single(repo.Records);
        Assert.Equal("Active", repo.Records[0].Status);
        Assert.Equal("bid_501", repo.Records[0].KotobeeBookId);
        Assert.Equal("full", repo.Records[0].BookAccessState);
    }

    [Fact]
    public async Task FulfillPaidOrder_WhenBookAlreadyActiveLocally_SkipsDuplicateCall()
    {
        var verifier = new StubPaidOrderVerifier { IsPaid = true };
        var client = new MockKotobeeApiClient();
        var repo = new InMemoryEntitlementRepo();
        repo.Records.Add(new KotobeeEntitlementRecord(
            Id: Guid.NewGuid(),
            OrderId: "ord_paid_idempotent",
            ProductSku: "SKU-BOOK-A",
            BuyerEmail: "buyer@example.com",
            KotobeeUid: "koto_uid_123",
            KotobeeMode: "library",
            KotobeeLibId: "100",
            KotobeeBookId: "bid_501",
            Status: "Active",
            BookAccessState: "full",
            AttemptCount: 1,
            LastError: null,
            ExpiresAtUtc: null,
            ActivatedAtUtc: DateTimeOffset.UtcNow));

        var options = new KotobeeOptions
        {
            DefaultLibraryId = "100",
            ProductMappings = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SKU-BOOK-A"] = [new KotobeeProductBookMapping("bid_501")]
            }
        };
        var mappingResolver = new KotobeeCatalogMappingResolver(new OptionsMonitorStub(options));
        var service = new FulfillKotobeeOrderService(
            verifier,
            client,
            repo,
            mappingResolver,
            new OptionsMonitorStub(options),
            NullLogger<FulfillKotobeeOrderService>.Instance);

        var job = new KotobeeFulfillmentJob("ord_paid_idempotent", "buyer@example.com", "Maria Clara", ["SKU-BOOK-A"]);
        await service.FulfillPaidOrderAsync(job);

        // Skips calling AddUser since book is already Active locally
        Assert.Equal(0, client.AddUserCallCount);
    }

    [Fact]
    public void CatalogMappingResolver_DeduplicatesBundleAndIndividualBooks()
    {
        var options = new KotobeeOptions
        {
            DefaultLibraryId = "100",
            ProductMappings = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SKU-BOOK-A"] = [new KotobeeProductBookMapping("bid_501")],
                ["SKU-BUNDLE-ALL"] = [
                    new KotobeeProductBookMapping("bid_501"),
                    new KotobeeProductBookMapping("bid_502")
                ]
            }
        };
        var resolver = new KotobeeCatalogMappingResolver(new OptionsMonitorStub(options));
        var targets = resolver.ResolveBooksForOrder(["SKU-BOOK-A", "SKU-BUNDLE-ALL"]);

        Assert.Equal(2, targets.Count);
        Assert.Equal("bid_501", targets[0].BookId);
        Assert.Equal("bid_502", targets[1].BookId);
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<KotobeeOptions>
    {
        public KotobeeOptions CurrentValue { get; }
        public KotobeeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<KotobeeOptions, string?> listener) => null;
        public OptionsMonitorStub(KotobeeOptions value) => CurrentValue = value;
    }
}
