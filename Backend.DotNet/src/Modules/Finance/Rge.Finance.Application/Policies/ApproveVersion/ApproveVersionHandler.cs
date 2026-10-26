using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Policies.AuthorVersion;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.Policies.ApproveVersion;

public sealed record ApprovePolicyVersionCommand(
    string VersionId,
    string ApproverId,
    string? Comments = null) : ICommand<PolicyVersionDto>;

public sealed class ApproveVersionHandler : ICommandHandler<ApprovePolicyVersionCommand, PolicyVersionDto>
{
    private readonly IPolicyRepository _repository;
    private readonly IClock _clock;

    public ApproveVersionHandler(IPolicyRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PolicyVersionDto>> HandleAsync(
        ApprovePolicyVersionCommand command,
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
        Result approveResult = version.Approve(command.ApproverId, nowUtc);
        if (approveResult.IsFailure)
        {
            return Result.Failure<PolicyVersionDto>(approveResult.Error);
        }

        await _repository.SaveVersionAsync(version, cancellationToken).ConfigureAwait(false);

        // Update current version on the policy
        IReadOnlyList<FinancialPolicy> policies = await _repository.ListPoliciesAsync(cancellationToken).ConfigureAwait(false);
        FinancialPolicy? parentPolicy = policies.FirstOrDefault(p => string.Equals(p.Id, version.PolicyId, StringComparison.OrdinalIgnoreCase));
        if (parentPolicy is not null)
        {
            parentPolicy.SetCurrentVersion(version.Id, nowUtc);
            await _repository.SavePolicyAsync(parentPolicy, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(AuthorVersionHandler.MapToDto(version));
    }
}
