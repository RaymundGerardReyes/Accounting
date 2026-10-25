using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Domain.Orders;

namespace Rge.Tests.Common.Builders;

/// <summary>
/// Fluent test builder for Order aggregate.
/// </summary>
public sealed class OrderBuilder
{
    private string _orderNumber = "ORD-202610-0001";
    private string _title = "CPA Review Materials - Batch 2026";
    private string _category = "product_order";
    private string _referenceCode = "REF-2026-X1";
    private Money _amount = Money.Php(1500_00);
    private string _metadataJson = "{\"department\":\"Tax\",\"cost_center\":\"CC-101\"}";
    private DateTimeOffset _nowUtc = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);
    private string _userId = "acme_admin";

    public OrderBuilder WithOrderNumber(string orderNumber)
    {
        _orderNumber = orderNumber;
        return this;
    }

    public OrderBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public OrderBuilder WithCategory(string category)
    {
        _category = category;
        return this;
    }

    public OrderBuilder WithReferenceCode(string referenceCode)
    {
        _referenceCode = referenceCode;
        return this;
    }

    public OrderBuilder WithAmount(Money amount)
    {
        _amount = amount;
        return this;
    }

    public OrderBuilder WithUserId(string userId)
    {
        _userId = userId;
        return this;
    }

    public OrderBuilder WithTimestamp(DateTimeOffset nowUtc)
    {
        _nowUtc = nowUtc;
        return this;
    }

    public Order BuildPending() =>
        Order.Create(_orderNumber, _title, _category, _referenceCode, _amount, _metadataJson, _nowUtc, _userId);

    public Order BuildPaid()
    {
        var order = BuildPending();
        order.MarkPaid(_nowUtc.AddMinutes(5));
        return order;
    }
}

