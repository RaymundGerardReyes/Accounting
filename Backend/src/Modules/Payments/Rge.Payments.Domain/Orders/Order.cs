using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Domain.Errors;

namespace Rge.Payments.Domain.Orders;

/// <summary>
/// Represents a commercial bookstore order (product_order) or internal department invoice (department_invoice).
/// </summary>
public sealed class Order : AggregateRoot<string>
{
    public string UserId { get; private set; }
    public string OrderNumber { get; private set; }
    public string Title { get; private set; }
    public string Category { get; private set; }
    public string ReferenceCode { get; private set; }
    public Money Amount { get; private set; }
    public string Status { get; private set; }
    public string MetadataJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Order(
        string id,
        string userId,
        string orderNumber,
        string title,
        string category,
        string referenceCode,
        Money amount,
        string status,
        string metadataJson,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) : base(id)
    {
        UserId = Guard.AgainstNullOrWhiteSpace(userId);
        OrderNumber = Guard.AgainstNullOrWhiteSpace(orderNumber);
        Title = Guard.AgainstNullOrWhiteSpace(title);
        Category = Guard.AgainstNullOrWhiteSpace(category);
        ReferenceCode = Guard.AgainstNullOrWhiteSpace(referenceCode);
        Guard.AgainstNonPositive(amount.AmountMinor, nameof(amount));
        Amount = amount;
        Status = status;
        MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static Order Create(
        string orderNumber,
        string title,
        string category,
        string referenceCode,
        Money authoritativeAmount,
        string metadataJson,
        DateTimeOffset nowUtc,
        string userId = "acme_admin")
    {
        return new Order(
            id: Guid.NewGuid().ToString("D"),
            userId: userId,
            orderNumber: orderNumber,
            title: title,
            category: category,
            referenceCode: referenceCode,
            amount: authoritativeAmount,
            status: "pending",
            metadataJson: metadataJson,
            createdAt: nowUtc,
            updatedAt: nowUtc);
    }

    public static Order Rehydrate(
        string id,
        string userId,
        string orderNumber,
        string title,
        string category,
        string referenceCode,
        Money amount,
        string status,
        string metadataJson,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new Order(id, userId, orderNumber, title, category, referenceCode, amount, status, metadataJson, createdAt, updatedAt);
    }

    public Result MarkPaid(DateTimeOffset paidAtUtc)
    {
        if (Status == "paid")
        {
            return Result.Failure(PaymentErrors.OrderAlreadyPaid(OrderNumber));
        }

        Status = "paid";
        UpdatedAt = paidAtUtc;
        return Result.Success();
    }

    public Result UpdateDetails(
        string? title,
        Money? newAmount,
        string? metadataJson,
        DateTimeOffset updatedAtUtc)
    {
        if (Status == "paid")
        {
            return Result.Failure(PaymentErrors.OrderAlreadyPaid(OrderNumber));
        }

        if (Status == "cancelled")
        {
            return Result.Failure(Error.Conflict("Payments.OrderCancelled", $"Order '{OrderNumber}' has been cancelled and cannot be updated."));
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }

        if (newAmount is not null)
        {
            Guard.AgainstNonPositive(newAmount.Value.AmountMinor, nameof(newAmount));
            Amount = newAmount.Value;
        }

        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            MetadataJson = metadataJson;
        }

        UpdatedAt = updatedAtUtc;
        return Result.Success();
    }

    public Result Cancel(string reason, DateTimeOffset cancelledAtUtc)
    {
        if (Status == "paid")
        {
            return Result.Failure(PaymentErrors.OrderAlreadyPaid(OrderNumber));
        }

        if (Status == "cancelled")
        {
            return Result.Success(); // Idempotent
        }

        Status = "cancelled";
        UpdatedAt = cancelledAtUtc;
        return Result.Success();
    }
}

