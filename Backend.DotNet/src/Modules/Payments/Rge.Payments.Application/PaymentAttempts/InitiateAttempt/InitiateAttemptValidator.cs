using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Payments.Application.PaymentAttempts.InitiateAttempt;

public sealed class InitiateAttemptValidator : IValidator<InitiateAttemptCommand>
{
    public Result Validate(InitiateAttemptCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            return Result.Failure(Error.Validation("InitiateAttempt.OrderIdRequired", "Order ID is required."));
        }

        return Result.Success();
    }
}
