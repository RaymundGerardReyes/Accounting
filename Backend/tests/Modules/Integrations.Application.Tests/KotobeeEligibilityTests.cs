using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Infrastructure.Kotobee;
using Rge.Payments.Contracts.Abstractions;
using Xunit;

namespace Rge.Integrations.Application.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public sealed class KotobeeEligibilityTests
{
    private sealed class FakeOrderLookup : IOrderLookup
    {
        private readonly Dictionary<string, OrderLookupDto> _orders = new(StringComparer.OrdinalIgnoreCase);

        public void Add(OrderLookupDto order)
        {
            _orders[order.OrderId] = order;
            _orders[order.OrderNumber] = order;
        }

        public Task<OrderLookupDto?> GetOrderByIdAsync(string orderId, CancellationToken cancellationToken = default)
        {
            _orders.TryGetValue(orderId, out var o);
            return Task.FromResult(o);
        }

        public Task<OrderLookupDto?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
        {
            _orders.TryGetValue(orderNumber, out var o);
            return Task.FromResult(o);
        }
    }

    private static KotobeeCatalogMappingResolver CreateResolver(Dictionary<string, List<KotobeeProductBookMapping>> mappings)
    {
        var options = new KotobeeOptions
        {
            Enabled = true,
            ProductMappings = mappings
        };
        var monitor = new TestOptionsMonitor<KotobeeOptions>(options);
        return new KotobeeCatalogMappingResolver(monitor);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    [Fact]
    public async Task EligibilityChecker_ReturnsFalse_WhenOrderIdIsBlank()
    {
        var resolver = CreateResolver([]);
        var checker = new OrderKotobeeEligibilityChecker(resolver, NullLogger<OrderKotobeeEligibilityChecker>.Instance);

        var result = await checker.OrderHasMappedKotobeeItemsAsync("   ");
        Assert.False(result);
    }

    [Fact]
    public async Task EligibilityChecker_ReturnsTrue_WhenOrderLookupIsNull()
    {
        var resolver = CreateResolver([]);
        var checker = new OrderKotobeeEligibilityChecker(resolver, NullLogger<OrderKotobeeEligibilityChecker>.Instance, null);

        var result = await checker.OrderHasMappedKotobeeItemsAsync("ORD-123");
        Assert.True(result);
    }

    [Fact]
    public async Task EligibilityChecker_ReturnsFalse_WhenOrderNotFound()
    {
        var resolver = CreateResolver([]);
        var fakeLookup = new FakeOrderLookup();
        var checker = new OrderKotobeeEligibilityChecker(resolver, NullLogger<OrderKotobeeEligibilityChecker>.Instance, fakeLookup);

        var result = await checker.OrderHasMappedKotobeeItemsAsync("ORD-NOTFOUND");
        Assert.False(result);
    }

    [Fact]
    public async Task EligibilityChecker_ReturnsTrue_WhenOrderHasMappedSkuInReferenceCode()
    {
        var resolver = CreateResolver(new()
        {
            ["SKU-EBOOK-1"] = [new KotobeeProductBookMapping("501")]
        });
        var fakeLookup = new FakeOrderLookup();
        fakeLookup.Add(new OrderLookupDto(
            OrderId: "ORD-001",
            OrderNumber: "NUM-001",
            Title: "General Consultation",
            Category: "Service",
            ReferenceCode: "SKU-EBOOK-1",
            AmountMinor: 150000,
            Currency: "PHP",
            Status: "Paid",
            MetadataJson: "{}",
            CreatedAtUtc: DateTimeOffset.UtcNow
        ));

        var checker = new OrderKotobeeEligibilityChecker(resolver, NullLogger<OrderKotobeeEligibilityChecker>.Instance, fakeLookup);
        var result = await checker.OrderHasMappedKotobeeItemsAsync("ORD-001");

        Assert.True(result);
    }

    [Fact]
    public async Task EligibilityChecker_ReturnsTrue_WhenOrderHasMappedSkuInMetadataJson()
    {
        var resolver = CreateResolver(new()
        {
            ["SKU-EBOOK-2"] = [new KotobeeProductBookMapping("502")]
        });
        var fakeLookup = new FakeOrderLookup();
        fakeLookup.Add(new OrderLookupDto(
            OrderId: "ORD-002",
            OrderNumber: "NUM-002",
            Title: "Invoice #2",
            Category: "Service",
            ReferenceCode: "REF-CONSULT",
            AmountMinor: 150000,
            Currency: "PHP",
            Status: "Paid",
            MetadataJson: "{\"line_items\": [{\"sku\": \"SKU-EBOOK-2\"}]}",
            CreatedAtUtc: DateTimeOffset.UtcNow
        ));

        var checker = new OrderKotobeeEligibilityChecker(resolver, NullLogger<OrderKotobeeEligibilityChecker>.Instance, fakeLookup);
        var result = await checker.OrderHasMappedKotobeeItemsAsync("ORD-002");

        Assert.True(result);
    }

    [Fact]
    public async Task EligibilityChecker_ReturnsFalse_WhenOrderHasOnlyNonEbookSkus()
    {
        var resolver = CreateResolver(new()
        {
            ["SKU-EBOOK-1"] = [new KotobeeProductBookMapping("501")]
        });
        var fakeLookup = new FakeOrderLookup();
        fakeLookup.Add(new OrderLookupDto(
            OrderId: "ORD-003",
            OrderNumber: "NUM-003",
            Title: "Accounting Retainer",
            Category: "Retainer",
            ReferenceCode: "SKU-ACCOUNTING-SVC",
            AmountMinor: 500000,
            Currency: "PHP",
            Status: "Paid",
            MetadataJson: "{\"skus\": [\"SKU-TAX-AUDIT\", \"SKU-PAYROLL\"]}",
            CreatedAtUtc: DateTimeOffset.UtcNow
        ));

        var checker = new OrderKotobeeEligibilityChecker(resolver, NullLogger<OrderKotobeeEligibilityChecker>.Instance, fakeLookup);
        var result = await checker.OrderHasMappedKotobeeItemsAsync("ORD-003");

        Assert.False(result);
    }
}
