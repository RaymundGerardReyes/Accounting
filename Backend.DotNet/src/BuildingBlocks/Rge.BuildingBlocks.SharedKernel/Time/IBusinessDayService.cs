namespace Rge.BuildingBlocks.SharedKernel.Time;

/// <summary>
/// Calculates Philippine business days (excluding Saturdays, Sundays, and declared PH holidays)
/// for Finance SLA tracking and liquidation due dates.
/// </summary>
public interface IBusinessDayService
{
    bool IsBusinessDay(DateOnly date);
    DateOnly AddBusinessDays(DateOnly startDate, int businessDaysToAdd);
    DateOnly GetSubmissionDeadlineNextMonth(int year, int month, int deadlineDayOfNextMonth);
}

