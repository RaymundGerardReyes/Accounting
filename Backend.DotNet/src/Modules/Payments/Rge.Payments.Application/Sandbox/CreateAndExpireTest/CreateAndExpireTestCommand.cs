using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.Sandbox.CreateAndExpireTest;

public sealed record CreateAndExpireTestCommand(string Channel = "qrph") : ICommand<CreateAndExpireTestResponse>;

public sealed record CreateAndExpireTestResponse(
    bool Success,
    OrderDto Order,
    PaymentAttemptDto Attempt,
    PaymentAttemptDto ExpiredAttempt,
    string Message);
