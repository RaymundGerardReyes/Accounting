using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;

using Rge.Identity.Domain;



namespace Rge.Identity.Application.ListFinanceUsers;



public sealed record ListFinanceUsersQuery : IQuery<IReadOnlyList<FinanceUserSummaryDto>>;



public sealed record FinanceUserSummaryDto(

    string Id,

    string EmployeeCode,

    string FullName,

    string Email,

    string DepartmentId,

    string CostCenterId,

    string EmployeeGroup,

    string Status,

    IReadOnlySet<string> ActiveRoles);



public sealed class ListFinanceUsersHandler : IQueryHandler<ListFinanceUsersQuery, IReadOnlyList<FinanceUserSummaryDto>>

{

    private readonly IRoleStore _roleStore;



    public ListFinanceUsersHandler(IRoleStore roleStore)

    {

        _roleStore = roleStore;

    }



    public async Task<Result<IReadOnlyList<FinanceUserSummaryDto>>> HandleAsync(

        ListFinanceUsersQuery query,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Actor> actors = await _roleStore

            .ListFinanceUsersAsync(cancellationToken)

            .ConfigureAwait(false);



        List<FinanceUserSummaryDto> summaries = actors

            .Select(a => new FinanceUserSummaryDto(

                Id: a.Id,

                EmployeeCode: a.EmployeeCode,

                FullName: a.FullName,

                Email: a.Email,

                DepartmentId: a.DepartmentId,

                CostCenterId: a.CostCenterId,

                EmployeeGroup: a.EmployeeGroup,

                Status: a.Status,

                ActiveRoles: a.GetActiveRoleNames()))

            .ToList();



        return Result.Success<IReadOnlyList<FinanceUserSummaryDto>>(summaries);

    }

}
