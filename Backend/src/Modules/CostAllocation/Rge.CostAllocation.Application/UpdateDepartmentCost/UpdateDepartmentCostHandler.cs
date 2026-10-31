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
    private readonly ICostAllocationStore _store;

    public UpdateDepartmentCostHandler(ICostAllocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<Result<CostDepartmentDto>> HandleAsync(
        UpdateDepartmentCostCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.DepartmentName))
        {
            return Task.FromResult(Result.Failure<CostDepartmentDto>(
                Error.Validation("CostAllocation.MissingDepartment", "Department name is required.")));
        }

        if (command.DirectCostMinor < 0 || command.MachineHours < 0 || command.LaborHours < 0 || command.OutputVolume < 0)
        {
            return Task.FromResult(Result.Failure<CostDepartmentDto>(
                Error.Validation("CostAllocation.NegativeMetric", "Cost and driver metrics cannot be negative.")));
        }

        CostDepartmentDto? updated = _store.UpdateDepartmentMetrics(
            command.DepartmentName.Trim(),
            command.DirectCostMinor,
            command.MachineHours,
            command.LaborHours,
            command.OutputVolume);

        if (updated is null)
        {
            return Task.FromResult(Result.Failure<CostDepartmentDto>(
                Error.NotFound("CostAllocation.DepartmentNotFound", $"Department '{command.DepartmentName}' was not found.")));
        }

        return Task.FromResult(Result.Success(updated));
    }
}

