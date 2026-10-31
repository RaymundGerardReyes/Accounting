using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Application.Orders.CreateOrder;

public sealed class CreateOrderValidator : IValidator<CreateOrderCommand>
{
    public Result Validate(CreateOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return Result.Failure(Error.Validation("CreateOrder.TitleRequired", "Order title is required."));
        }

        if (string.IsNullOrWhiteSpace(command.Category) ||
            (!string.Equals(command.Category, OrderCategory.ProductOrder, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(command.Category, OrderCategory.DepartmentInvoice, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure(Error.Validation("CreateOrder.InvalidCategory", "Category must be 'product_order' or 'department_invoice'."));
        }

        if (string.IsNullOrWhiteSpace(command.ReferenceCode))
        {
            return Result.Failure(Error.Validation("CreateOrder.ReferenceCodeRequired", "Reference code is required."));
        }

        if (command.Quantity <= 0)
        {
            return Result.Failure(Error.Validation("CreateOrder.InvalidQuantity", "Quantity must be at least 1."));
        }

        return Result.Success();
    }
}

