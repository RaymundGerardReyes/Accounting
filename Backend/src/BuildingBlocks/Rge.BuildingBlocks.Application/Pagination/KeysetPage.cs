using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Rge.BuildingBlocks.Application.Pagination;

public sealed record KeysetCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public string Encode()
    {
        var raw = $"{CreatedAt.UtcTicks:D20}|{Id:D}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

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

    public static bool TryDecode(string? token, out KeysetCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var padded = token.Trim().Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = decoded.Split('|', 2);
            if (parts.Length != 2) return false;

            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var utcTicks))
                return false;

            if (!Guid.TryParse(parts[1], out var id))
                return false;

            cursor = new KeysetCursor(new DateTimeOffset(utcTicks, TimeSpan.Zero), id);
            return true;
        }
        catch
        {
            return false;
        }
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

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Standard factory pattern")]
public sealed record KeysetPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore)
{
    /// <summary>
    /// Builds a KeysetPage from a query that requested (limit + 1) parent records.
    /// </summary>
    public static KeysetPage<T> FromPlusOne(
        IReadOnlyList<T> fetchedItems,
        int requestedLimit,
        Func<T, KeysetCursor> cursorSelector)
    {
        ArgumentNullException.ThrowIfNull(fetchedItems);
        ArgumentNullException.ThrowIfNull(cursorSelector);

        var safeLimit = Math.Clamp(requestedLimit, 1, PageRequest.MaxPageSize);
        var hasMore = fetchedItems.Count > safeLimit;

        IReadOnlyList<T> pageItems = hasMore
            ? fetchedItems is List<T> list
                ? list.GetRange(0, safeLimit)
                : new List<T>(fetchedItems).GetRange(0, safeLimit)
            : fetchedItems;

        var nextCursor = hasMore && pageItems.Count > 0
            ? cursorSelector(pageItems[^1]).Encode()
            : null;

        return new KeysetPage<T>(pageItems, nextCursor, hasMore);
    }
}
