using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.PaymentAttempts.ExpireAttempt;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Application.Sandbox.CreateAndExpireTest;

public sealed class CreateAndExpireTestHandler : ICommandHandler<CreateAndExpireTestCommand, CreateAndExpireTestResponse>
{
    private readonly CreateOrderHandler _createOrderHandler;
    private readonly InitiateAttemptHandler _initiateAttemptHandler;
    private readonly ExpireAttemptHandler _expireAttemptHandler;

    public CreateAndExpireTestHandler(
        CreateOrderHandler createOrderHandler,
        InitiateAttemptHandler initiateAttemptHandler,
        ExpireAttemptHandler expireAttemptHandler)
    {
        ArgumentNullException.ThrowIfNull(createOrderHandler);
        ArgumentNullException.ThrowIfNull(initiateAttemptHandler);
        ArgumentNullException.ThrowIfNull(expireAttemptHandler);

        _createOrderHandler = createOrderHandler;
        _initiateAttemptHandler = initiateAttemptHandler;
        _expireAttemptHandler = expireAttemptHandler;
    }

    public async Task<Result<CreateAndExpireTestResponse>> HandleAsync(
        CreateAndExpireTestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string testSuffix = Guid.NewGuid().ToString("N")[..8];
        var createOrderCmd = new CreateOrderCommand(
            Title: $"Test Expiry Order {testSuffix}",
            Category: OrderCategory.ProductOrder,
            ReferenceCode: "Product A",
            AmountMinor: 12500,
            Quantity: 1,
            CustomerName: "Test Sandbox User",
            CustomerEmail: "sandbox-tester@rge-accounting.ph");

        var orderRes = await _createOrderHandler.HandleAsync(createOrderCmd, cancellationToken).ConfigureAwait(false);
        if (orderRes.IsFailure)
        {
            return Result.Failure<CreateAndExpireTestResponse>(orderRes.Error);
        }

        var initCmd = new InitiateAttemptCommand(
            OrderId: orderRes.Value.Id,
            PaymentMethod: command.Channel,
            IdempotencyKey: $"idem-test-expire-{testSuffix}");

        var initRes = await _initiateAttemptHandler.HandleAsync(initCmd, cancellationToken).ConfigureAwait(false);
        if (initRes.IsFailure)
        {
            return Result.Failure<CreateAndExpireTestResponse>(initRes.Error);
        }

        var expireCmd = new ExpireAttemptCommand(
            PaymentId: initRes.Value.Id,
            Reason: "Sandbox automated expiration test");

        var expireRes = await _expireAttemptHandler.HandleAsync(expireCmd, cancellationToken).ConfigureAwait(false);
        if (expireRes.IsFailure)
        {
            return Result.Failure<CreateAndExpireTestResponse>(expireRes.Error);
        }

        return Result.Success(new CreateAndExpireTestResponse(
            Success: true,
            Order: orderRes.Value,
            Attempt: initRes.Value,
            ExpiredAttempt: expireRes.Value.Attempt,
            Message: "Fresh test order created and authoritatively expired."));
    }
}
