using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Payments.Domain.Errors;

public static class OrderErrors
{
    public static Error NotFound(string orderId) =>
        Error.NotFound("Orders.NotFound", $"Order '{orderId}' was not found.");

    public static Error AlreadyPaid(string orderNumber) =>
        Error.Conflict("Orders.AlreadyPaid", $"Order '{orderNumber}' is already fully paid.");

    public static Error AlreadyCancelled(string orderNumber) =>
        Error.Conflict("Orders.AlreadyCancelled", $"Order '{orderNumber}' has been cancelled and cannot accept payment attempts.");

    public static Error InvalidAmount(long amountMinor) =>
        Error.Validation("Orders.InvalidAmount", $"Order amount ({amountMinor}) must be greater than zero.");

    public static Error InvalidCategory(string category) =>
        Error.Validation("Orders.InvalidCategory", $"Invalid order category '{category}'.");

    public static Error UnknownReferenceCode(string referenceCode) =>
        Error.Validation("Orders.UnknownReferenceCode", $"Product or department reference '{referenceCode}' was not found in the authoritative catalog.");
}


