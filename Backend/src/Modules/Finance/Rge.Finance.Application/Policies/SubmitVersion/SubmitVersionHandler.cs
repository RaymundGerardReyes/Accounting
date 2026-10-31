using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Policies.AuthorVersion;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.Policies.SubmitVersion;

public sealed record SubmitPolicyVersionCommand(
    string VersionId,
    string ActorId) : ICommand<PolicyVersionDto>;

public sealed class SubmitVersionHandler : ICommandHandler<SubmitPolicyVersionCommand, PolicyVersionDto>
{
    private readonly IPolicyRepository _repository;
    private readonly IClock _clock;

    public SubmitVersionHandler(IPolicyRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PolicyVersionDto>> HandleAsync(
        SubmitPolicyVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PolicyVersion? version = await _repository.GetVersionByIdAsync(command.VersionId, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return Result.Failure<PolicyVersionDto>(Error.NotFound(
                "Finance.PolicyVersion.NotFound",
                $"Policy version '{command.VersionId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result submitResult = version.Submit(nowUtc);
        if (submitResult.IsFailure)
        {
            return Result.Failure<PolicyVersionDto>(submitResult.Error);
        }

        await _repository.SaveVersionAsync(version, cancellationToken).ConfigureAwait(false);

        return Result.Success(AuthorVersionHandler.MapToDto(version));
    }
}
