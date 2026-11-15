using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Rge.BuildingBlocks.Application.Pagination;

public sealed record PageRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; }
    public int PageSize { get; }
    public string? Status { get; }
    public string? Category { get; }
    public string? Search { get; }
    public string? SortBy { get; }
    public string SortDir { get; }

    public PageRequest(
        int? page = DefaultPage,
        int? pageSize = DefaultPageSize,
        string? status = null,
        string? category = null,
        string? search = null,
        string? sortBy = null,
        string? sortDir = "desc")
    {
        Page = Math.Max(DefaultPage, page ?? DefaultPage);
        PageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        Status = NormalizeFilter(status);
        Category = NormalizeFilter(category);
        Search = NormalizeSearch(search);
        SortBy = string.IsNullOrWhiteSpace(sortBy) ? "createdAt" : sortBy.Trim();
        SortDir = string.Equals(sortDir?.Trim(), "asc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";
    }

    /// <summary>
    /// Backward-compatibility constructor for single-cursor / limit queries.
    /// </summary>
    public PageRequest(string? cursor, int limit = 50)
        : this(page: 1, pageSize: limit, search: cursor)
    {
    }

    /// <summary>
    /// Overflow-safe SQL OFFSET calculation.
    /// </summary>
    public int Offset => (int)Math.Min(int.MaxValue, (long)(Page - 1) * PageSize);

    public int ClampedLimit => PageSize;

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return string.Equals(trimmed, "All", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }
}

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Standard factory pattern")]
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage)
{
    public static PagedResult<T> Create(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount)
    {
        var safePageSize = Math.Clamp(pageSize, 1, PageRequest.MaxPageSize);
        var safeTotalCount = Math.Max(0, totalCount);
        var totalPages = Math.Max(1, (int)Math.Ceiling(safeTotalCount / (double)safePageSize));
        var safePage = Math.Max(1, page);

        return new PagedResult<T>(
            Items: items,
            Page: safePage,
            PageSize: safePageSize,
            TotalCount: safeTotalCount,
            TotalPages: totalPages,
            HasNextPage: safePage < totalPages,
            HasPreviousPage: safePage > 1);
    }
}
