using Rge.Finance.Application.Reporting.Tracker;

namespace Rge.Finance.Infrastructure.Reporting;

public sealed class TrackerReadService
{
    private readonly TrackerHandler _handler;

    public TrackerReadService(TrackerHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public Task<BuildingBlocks.SharedKernel.Results.Result<IReadOnlyList<TrackerItemDto>>> GetTrackerAsync(
        GetCentralizedTrackerQuery query,
        CancellationToken cancellationToken = default)
    {
        return _handler.HandleAsync(query, cancellationToken);
    }
}
