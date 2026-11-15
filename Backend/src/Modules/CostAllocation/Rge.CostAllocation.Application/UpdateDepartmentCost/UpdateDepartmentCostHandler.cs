using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.CostAllocation.Application.Ports;

namespace Rge.CostAllocation.Application.UpdateDepartmentCost;

public sealed record UpdateDepartmentCostCommand(
    string DepartmentName,
    long DirectCostMinor,
    long MachineHours,
    long LaborHours,
    long OutputVolume) : ICommand<CostDepartmentDto>;

public sealed class UpdateDepartmentCostHandler : ICommandHandler<UpdateDepartmentCostCommand, CostDepartmentDto>
{
    private readonly ICostAllocationPostgresStore _store;

    public UpdateDepartmentCostHandler(ICostAllocationPostgresStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<Result<CostDepartmentDto>> HandleAsync(
        UpdateDepartmentCostCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.DepartmentName))
        {
            return Result.Failure<CostDepartmentDto>(
                Error.Validation("CostAllocation.MissingDepartment", "Department name is required."));
        }

        if (command.DirectCostMinor < 0 || command.MachineHours < 0 || command.LaborHours < 0 || command.OutputVolume < 0)
        {
            return Result.Failure<CostDepartmentDto>(
                Error.Validation("CostAllocation.NegativeMetric", "Cost and driver metrics cannot be negative."));
        }

        IReadOnlyList<CostAllocationDepartmentRecord> departments =
            await _store.GetActiveDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        CostAllocationDepartmentRecord? department = departments.FirstOrDefault(
            x => string.Equals(x.DepartmentName, command.DepartmentName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.DepartmentId, command.DepartmentName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (department is null)
        {
            return Result.Failure<CostDepartmentDto>(
                Error.NotFound("CostAllocation.DepartmentNotFound", $"Department '{command.DepartmentName}' was not found."));
        }

        return await UpdateAsync(department, command.DirectCostMinor, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<CostDepartmentDto>> UpdateAsync(
        CostAllocationDepartmentRecord department,
        long directCostMinor,
        CancellationToken cancellationToken)
    {
        bool updated = await _store.UpdateDepartmentDirectCostAsync(
            department.DepartmentId, directCostMinor, cancellationToken).ConfigureAwait(false);
        if (!updated)
            return Result.Failure<CostDepartmentDto>(
                Error.Validation("CostAllocation.BudgetCapExceeded", "Direct cost cannot exceed the effective budget cap."));
        return Result.Success(new CostDepartmentDto(
            department.DepartmentId, department.DepartmentName, department.CostCenterId,
            directCostMinor, 0, directCostMinor, department.BudgetCapCentavos,
            department.BudgetCapCentavos - directCostMinor,
            department.HeadcountDriverWeight, 0, 0));
    }
}

