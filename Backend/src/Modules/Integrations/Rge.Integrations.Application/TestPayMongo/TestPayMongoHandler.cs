using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Application.TestPayMongo;

public sealed record TestPayMongoCommand(PayMongoTestRequest Request) : ICommand<PayMongoTestResponse>;

public sealed class TestPayMongoHandler : ICommandHandler<TestPayMongoCommand, PayMongoTestResponse>
{
    private readonly IPayMongoProbe _probe;

    public TestPayMongoHandler(IPayMongoProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public async Task<Result<PayMongoTestResponse>> HandleAsync(
        TestPayMongoCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        PayMongoTestResponse response = await _probe
            .TestPayMongoAsync(command.Request, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

