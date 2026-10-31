namespace Rge.BuildingBlocks.Application.Pagination;

public sealed record PageRequest(string? Cursor = null, int Limit = 50)
{
    public int ClampedLimit => Math.Clamp(Limit, 1, 200);
}

public sealed record KeysetPage<TItem>(
    IReadOnlyList<TItem> Items,
    string? NextCursor,
    bool HasMore);
