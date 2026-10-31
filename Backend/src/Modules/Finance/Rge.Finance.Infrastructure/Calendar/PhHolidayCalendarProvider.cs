using System.Collections.Concurrent;
using System.Data.Common;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Time;

namespace Rge.Finance.Infrastructure.Calendar;

/// <summary>
/// Provides Philippine holiday dates loaded from database or initialized with standard PH statutory holidays.
/// </summary>
public sealed class PhHolidayCalendarProvider
{
    private static readonly HashSet<DateOnly> StatutoryHolidays =
    [
        new(2026, 1, 1),   // New Year's Day
        new(2026, 4, 2),   // Maundy Thursday
        new(2026, 4, 3),   // Good Friday
        new(2026, 4, 9),   // Day of Valor (Araw ng Kagitingan)
        new(2026, 5, 1),   // Labor Day
        new(2026, 6, 12),  // Independence Day
        new(2026, 8, 31),  // National Heroes Day
        new(2026, 11, 30), // Bonifacio Day
        new(2026, 12, 25), // Christmas Day
        new(2026, 12, 30), // Rizal Day
        new(2026, 12, 31)  // New Year's Eve
    ];

    private readonly ConcurrentDictionary<DateOnly, byte> _holidays = new();
    private readonly ConnectionScope? _connectionScope;

    public PhHolidayCalendarProvider(ConnectionScope? connectionScope = null)
    {
        _connectionScope = connectionScope;
        foreach (DateOnly holiday in StatutoryHolidays)
        {
            _holidays[holiday] = 1;
        }
    }

    public async Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionScope is not null)
        {
            try
            {
                DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using DbCommand cmd = connection.CreateCommand();
                cmd.Transaction = _connectionScope.CurrentTransaction;
                cmd.CommandText = "SELECT holiday_date FROM holiday_calendar;";
                cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;

                await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string dateStr = reader.GetString(0);
                    if (DateOnly.TryParse(dateStr, out DateOnly date))
                    {
                        _holidays[date] = 1;
                    }
                }
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException)
            {
                // Fall back to statutory holidays
            }
        }

        return _holidays.Keys.ToHashSet();
    }

    public PhilippineBusinessCalendar CreateCalendar()
    {
        return new PhilippineBusinessCalendar(_holidays.Keys);
    }
}
