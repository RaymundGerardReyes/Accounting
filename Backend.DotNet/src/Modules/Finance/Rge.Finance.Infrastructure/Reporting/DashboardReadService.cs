using Rge.Finance.Application.Reporting.DashboardSummary;

namespace Rge.Finance.Infrastructure.Reporting;

public sealed class DashboardReadService
{
    private readonly DashboardSummaryHandler _handler;

    public DashboardReadService(DashboardSummaryHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public Task<BuildingBlocks.SharedKernel.Results.Result<DashboardSummaryDto>> GetSummaryAsync(
        GetDashboardSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        return _handler.HandleAsync(query, cancellationToken);
    }
}
