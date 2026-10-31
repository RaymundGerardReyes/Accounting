using System.Globalization;
using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.Pricing;

namespace Rge.Payments.Application.Orders.CreateOrder;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _repository;
    private readonly IPricingPolicy _pricingPolicy;
    private readonly IClock _clock;

    public CreateOrderHandler(
        IOrderRepository repository,
        IPricingPolicy pricingPolicy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(pricingPolicy);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _pricingPolicy = pricingPolicy;
        _clock = clock;
    }

    public async Task<Result<OrderDto>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;

        string refCode = !string.IsNullOrWhiteSpace(command.ReferenceCode)
            ? command.ReferenceCode.Trim()
            : (command.Title ?? string.Empty).Trim();

        Result<Money> priceResult = _pricingPolicy.CalculateAuthoritativeAmount(
            category: command.Category,
            referenceCode: refCode,
            quantity: command.Quantity,
            clientSuppliedAmountMinor: command.AmountMinor);

        if (priceResult.IsFailure)
        {
            return Result.Failure<OrderDto>(priceResult.Error);
        }

        string prefix = string.Equals(command.Category, OrderCategory.ProductOrder, StringComparison.OrdinalIgnoreCase)
            ? "ORD"
            : "INV";
        string yearMonth = nowUtc.ToString("yyyyMM", CultureInfo.InvariantCulture);

        long sequence = await _repository.NextOrderSequenceAsync(prefix, yearMonth, cancellationToken).ConfigureAwait(false);
        string orderNumber = $"{prefix}-{yearMonth}-{sequence:D4}";

        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (command.Metadata is not null)
        {
            foreach (var kvp in command.Metadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(command.CustomerName))
        {
            metadata["customer_name"] = command.CustomerName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(command.CustomerEmail))
        {
            metadata["customer_email"] = command.CustomerEmail.Trim();
        }
        if (!string.IsNullOrWhiteSpace(command.CustomerPhone))
        {
            metadata["customer_phone"] = command.CustomerPhone.Trim();
        }

        string metadataJson = JsonSerializer.Serialize(metadata);

        string effectiveTitle = string.IsNullOrWhiteSpace(command.Title)
            ? $"Order for {refCode}"
            : command.Title.Trim();

        var order = Order.Create(
            orderNumber: orderNumber,
            title: effectiveTitle,
            category: command.Category.Trim().ToLowerInvariant(),
            referenceCode: refCode,
            authoritativeAmount: priceResult.Value,
            metadataJson: metadataJson,
            nowUtc: nowUtc,
            userId: string.IsNullOrWhiteSpace(command.UserId) ? "acme_admin" : command.UserId.Trim());

        await _repository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

        return Result.Success(order.ToDto());
    }
}

