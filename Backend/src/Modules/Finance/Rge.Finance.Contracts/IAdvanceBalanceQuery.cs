namespace Rge.Finance.Contracts;

public sealed record AdvanceBalanceDto(
    string EmployeeId,
    long TotalOutstandingMinor,
    int ActiveAdvancesCount,
    string Currency);

/// <summary>
/// Cross-module query abstraction for looking up employee cash advance balances and active count.
/// </summary>
public interface IAdvanceBalanceQuery
{
    Task<AdvanceBalanceDto> GetOutstandingBalanceAsync(string employeeId, CancellationToken cancellationToken = default);
}
