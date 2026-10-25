using System.Data.Common;

using System.Globalization;

using Rge.BuildingBlocks.SharedKernel.Money;



namespace Rge.BuildingBlocks.Infrastructure.Persistence;



/// <summary>

/// Provides zero-float type conversion helpers between PostgreSQL BIGINT/VARCHAR columns

/// and strongly typed C# domain value objects (Money, DateOnly).

/// </summary>

public static class DapperTypeHandlers

{

    public static Money ReadMoney(DbDataReader reader, int amountMinorOrdinal, int currencyOrdinal = -1)

    {

        ArgumentNullException.ThrowIfNull(reader);



        long amountMinor = reader.GetInt64(amountMinorOrdinal);

        string currency = currencyOrdinal >= 0 && !reader.IsDBNull(currencyOrdinal)

            ? reader.GetString(currencyOrdinal)

            : "PHP";



        return new Money(amountMinor, currency);

    }



    public static DateOnly ParseIsoDateOnly(string isoDate)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(isoDate);

        return DateOnly.ParseExact(isoDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    }



    public static string FormatIsoDateOnly(DateOnly date) =>

        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

}
