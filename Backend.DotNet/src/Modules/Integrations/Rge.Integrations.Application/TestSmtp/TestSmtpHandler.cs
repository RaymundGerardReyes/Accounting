using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Application.TestSmtp;

public sealed record TestSmtpCommand(EmailTestRequest Request) : ICommand<EmailTestResponse>;

public sealed class TestSmtpHandler : ICommandHandler<TestSmtpCommand, EmailTestResponse>
{
    private readonly ISmtpProbe _smtpProbe;

    public TestSmtpHandler(ISmtpProbe smtpProbe)
    {
        ArgumentNullException.ThrowIfNull(smtpProbe);
        _smtpProbe = smtpProbe;
    }

    public async Task<Result<EmailTestResponse>> HandleAsync(
        TestSmtpCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EmailTestResponse response = await _smtpProbe
            .TestSmtpAsync(command.Request, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

