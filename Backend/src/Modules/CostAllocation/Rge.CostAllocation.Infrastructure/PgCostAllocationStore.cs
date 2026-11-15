using Dapper;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.CostAllocation.Application.Ports;

namespace Rge.CostAllocation.Infrastructure;

public interface IPgCostAllocationStore : ICostAllocationPostgresStore
{
}

public sealed class PgCostAllocationStore(ConnectionScope connectionScope) :
    ICostAllocationPostgresStore, IPgCostAllocationStore
{
    public async Task<IReadOnlyList<CostAllocationDepartmentRecord>> GetActiveDepartmentsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT department_id AS DepartmentId,
                   rule_version_id AS RuleVersionId,
                   department_name AS DepartmentName,
                   cost_center_id AS CostCenterId,
                   direct_cost_centavos AS DirectCostCentavos,
                   budget_cap_centavos AS BudgetCapCentavos,
                   headcount_driver_weight AS HeadcountDriverWeight,
                   is_active AS IsActive
            FROM cost_allocation_departments
            WHERE is_active = TRUE
            ORDER BY department_id;
            """;
        var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<CostAllocationDepartmentRecord>(new CommandDefinition(
            sql,
            transaction: connectionScope.CurrentTransaction,
            commandTimeout: SqlTimeouts.StandardCommandSeconds,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<bool> UpdateDepartmentDirectCostAsync(
        string departmentId,
        long newDirectCostCentavos,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(departmentId);
        if (newDirectCostCentavos < 0)
            return false;

        const string sql = """
            UPDATE cost_allocation_departments
            SET direct_cost_centavos = @NewDirectCostCentavos,
                updated_at_utc = NOW()
            WHERE department_id = @DepartmentId
              AND @NewDirectCostCentavos <= budget_cap_centavos;
            """;
        var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        int affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { DepartmentId = departmentId.Trim(), NewDirectCostCentavos = newDirectCostCentavos },
            transaction: connectionScope.CurrentTransaction,
            commandTimeout: SqlTimeouts.StandardCommandSeconds,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return affected > 0;
    }
}
