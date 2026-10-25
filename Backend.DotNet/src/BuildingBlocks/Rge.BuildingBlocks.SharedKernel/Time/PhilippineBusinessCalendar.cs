namespace Rge.BuildingBlocks.SharedKernel.Time;

/// <summary>
/// Pure domain implementation of the Philippine business calendar.
/// Skips weekends and any dates present in the injected holiday set.
/// </summary>
public sealed class PhilippineBusinessCalendar : IBusinessDayService
{
    private readonly HashSet<DateOnly> _holidays;

    public PhilippineBusinessCalendar(IEnumerable<DateOnly>? holidays = null)
    {
        _holidays = holidays is not null
            ? new HashSet<DateOnly>(holidays)
            : [];
    }

    public bool IsBusinessDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        return !_holidays.Contains(date);
    }

    public DateOnly AddBusinessDays(DateOnly startDate, int businessDaysToAdd)
    {
        if (businessDaysToAdd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(businessDaysToAdd), "Business days to add cannot be negative.");
        }

        DateOnly current = startDate;
        int remaining = businessDaysToAdd;

        while (remaining > 0)
        {
            current = current.AddDays(1);
            if (IsBusinessDay(current))
            {
                remaining--;
            }
        }

        return current;
    }

    /// <summary>
    /// Computes the monthly expense submission deadline (e.g., Day 2 of the next month).
    /// If the target day falls on a weekend or PH holiday, rolls forward to the next business day.
    /// </summary>
    public DateOnly GetSubmissionDeadlineNextMonth(int year, int month, int deadlineDayOfNextMonth)
    {
        DateOnly firstOfCurrentMonth = new(year, month, 1);
        DateOnly nextMonth = firstOfCurrentMonth.AddMonths(1);
        int daysInNextMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
        int clampedDay = Math.Clamp(deadlineDayOfNextMonth, 1, daysInNextMonth);

        DateOnly candidate = new(nextMonth.Year, nextMonth.Month, clampedDay);
        while (!IsBusinessDay(candidate))
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }
}

