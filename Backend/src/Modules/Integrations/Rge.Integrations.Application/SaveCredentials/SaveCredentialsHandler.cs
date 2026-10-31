using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Application.SaveCredentials;

public sealed record SaveCredentialsCommand(SaveCredentialsRequest Request) : ICommand<SaveCredentialsResponse>;

public sealed class SaveCredentialsHandler : ICommandHandler<SaveCredentialsCommand, SaveCredentialsResponse>
{
    private readonly ISecretStore _secretStore;

    public SaveCredentialsHandler(ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        _secretStore = secretStore;
    }

    public async Task<Result<SaveCredentialsResponse>> HandleAsync(
        SaveCredentialsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        SaveCredentialsResponse response = await _secretStore
            .SaveCredentialsAsync(command.Request, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

