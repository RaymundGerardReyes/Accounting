using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Application.Orders.ManageOrder;

public sealed record UpdateOrderResponse(
    bool Success,
    string OrderId,
    string OrderNumber,
    string Title,
    long AmountMinor,
    string Status,
    string Message);

public sealed record UpdateOrderCommand(
    string OrderId,
    string? Title,
    long? AmountMinor,
    string? Currency,
    JsonElement? Metadata) : ICommand<UpdateOrderResponse>;

public sealed class UpdateOrderHandler : ICommandHandler<UpdateOrderCommand, UpdateOrderResponse>
{
    private readonly IOrderRepository _repository;
    private readonly IClock _clock;

    public UpdateOrderHandler(IOrderRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<UpdateOrderResponse>> HandleAsync(
        UpdateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            return Result.Failure<UpdateOrderResponse>(
                Error.Validation("Payments.OrderIdRequired", "Order ID must not be empty."));
        }

        string cleanId = command.OrderId.Trim();
        Order? order = await _repository.GetByIdAsync(cleanId, cancellationToken).ConfigureAwait(false)
            ?? await _repository.GetByOrderNumberAsync(cleanId, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<UpdateOrderResponse>(OrderErrors.NotFound(cleanId));
        }

        if (order.Status == "paid")
        {
            return Result.Failure<UpdateOrderResponse>(PaymentErrors.OrderAlreadyPaid(order.OrderNumber));
        }

        if (order.Status == "cancelled")
        {
            return Result.Failure<UpdateOrderResponse>(
                Error.Conflict("Payments.OrderCancelled", $"Order '{order.OrderNumber}' is cancelled and cannot be updated."));
        }

        Money? newAmount = null;
        if (command.AmountMinor.HasValue && command.AmountMinor.Value > 0)
        {
            newAmount = Money.Php(command.AmountMinor.Value);
        }

        string? metaJson = null;
        if (command.Metadata.HasValue)
        {
            metaJson = command.Metadata.Value.GetRawText();
        }

        var updateResult = order.UpdateDetails(
            title: command.Title,
            newAmount: newAmount,
            metadataJson: metaJson,
            updatedAtUtc: _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdateOrderResponse>(updateResult.Error);
        }

        await _repository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

        return Result.Success(new UpdateOrderResponse(
            Success: true,
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            Title: order.Title,
            AmountMinor: order.Amount.AmountMinor,
            Status: order.Status,
            Message: "Order updated successfully."));
    }
}

public sealed record CancelOrderResponse(
    bool Success,
    string OrderId,
    string OrderNumber,
    string Status,
    string Message);

public sealed record CancelOrderCommand(
    string OrderId,
    string? Reason) : ICommand<CancelOrderResponse>;

public sealed class CancelOrderHandler : ICommandHandler<CancelOrderCommand, CancelOrderResponse>
{
    private readonly IOrderRepository _repository;
    private readonly IClock _clock;

    public CancelOrderHandler(IOrderRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<CancelOrderResponse>> HandleAsync(
        CancelOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            return Result.Failure<CancelOrderResponse>(
                Error.Validation("Payments.OrderIdRequired", "Order ID must not be empty."));
        }

        string cleanId = command.OrderId.Trim();
        Order? order = await _repository.GetByIdAsync(cleanId, cancellationToken).ConfigureAwait(false)
            ?? await _repository.GetByOrderNumberAsync(cleanId, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<CancelOrderResponse>(OrderErrors.NotFound(cleanId));
        }

        var cancelResult = order.Cancel(command.Reason ?? "Cancelled by user", _clock.UtcNow);
        if (cancelResult.IsFailure)
        {
            return Result.Failure<CancelOrderResponse>(cancelResult.Error);
        }

        await _repository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

        return Result.Success(new CancelOrderResponse(
            Success: true,
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            Status: order.Status,
            Message: "Order cancelled successfully."));
    }
}

