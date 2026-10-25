using System.Globalization;

using System.Text;



namespace Rge.BuildingBlocks.Application.Pagination;



/// <summary>

/// Encodes and decodes keyset pagination cursors (TimestampTicks + EntityId)

/// so SQL queries use indexed seek predicates instead of slow OFFSET scans.

/// </summary>

public static class KeysetCursor

{

    public static string Encode(DateTimeOffset createdAtUtc, string id)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        string raw = $"{createdAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id}";

        byte[] bytes = Encoding.UTF8.GetBytes(raw);

        return Convert.ToBase64String(bytes)

            .Replace('+', '-')

            .Replace('/', '_')

            .TrimEnd('=');

    }



    public static bool TryDecode(string? cursor, out DateTimeOffset createdAtUtc, out string id)

    {

        createdAtUtc = default;

        id = string.Empty;



        if (string.IsNullOrWhiteSpace(cursor))

        {

            return false;

        }



        try

        {

            string padded = cursor.Replace('-', '+').Replace('_', '/');

            switch (padded.Length % 4)

            {

                case 2: padded += "=="; break;

                case 3: padded += "="; break;

            }



            byte[] bytes = Convert.FromBase64String(padded);

            string decoded = Encoding.UTF8.GetString(bytes);

            int separatorIndex = decoded.IndexOf('|', StringComparison.Ordinal);

            if (separatorIndex <= 0 || separatorIndex == decoded.Length - 1)

            {

                return false;

            }



            if (!long.TryParse(decoded.AsSpan(0, separatorIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))

            {

                return false;

            }



            createdAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero);

            id = decoded[(separatorIndex + 1)..];

            return !string.IsNullOrWhiteSpace(id);

        }

        catch (FormatException)

        {

            return false;

        }

    }

}
