using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.Policies.List;

public sealed record ListPoliciesQuery : IQuery<IReadOnlyList<FinancialPolicyDto>>;

public sealed record FinancialPolicyDto(
    string Id,
    string PolicyCode,
    string Name,
    string? Description,
    string ScopeWorkflow,
    string? CurrentVersionId,
    DateTimeOffset CreatedAtUtc);

public sealed class ListHandler : IQueryHandler<ListPoliciesQuery, IReadOnlyList<FinancialPolicyDto>>
{
    private readonly IPolicyRepository _repository;

    public ListHandler(IPolicyRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<FinancialPolicyDto>>> HandleAsync(
        ListPoliciesQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FinancialPolicy> policies = await _repository.ListPoliciesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<FinancialPolicyDto> dtos = policies.Select(p => new FinancialPolicyDto(
            Id: p.Id,
            PolicyCode: p.PolicyCode,
            Name: p.Name,
            Description: p.Description,
            ScopeWorkflow: p.ScopeWorkflow,
            CurrentVersionId: p.CurrentVersionId,
            CreatedAtUtc: p.CreatedAtUtc)).ToList();

        return Result.Success(dtos);
    }
}
