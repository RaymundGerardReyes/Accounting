using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Application.GetFormStatus;

public sealed record GetGoogleFormsStatusQuery : IQuery<GoogleFormsStatusResponse>;

public sealed class GetFormStatusHandler : IQueryHandler<GetGoogleFormsStatusQuery, GoogleFormsStatusResponse>
{
    private readonly IGoogleFormsSubmissionStore _store;
    private readonly IGoogleFormOrderBridge? _orderBridge;

    public GetFormStatusHandler(IGoogleFormsSubmissionStore store, IGoogleFormOrderBridge? orderBridge = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _orderBridge = orderBridge;
    }

    public async Task<Result<GoogleFormsStatusResponse>> HandleAsync(
        GetGoogleFormsStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        GoogleFormsStatusResponse response = await _store
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);

        if (_orderBridge is not null)
        {
            var (total, paid, pending) = await _orderBridge
                .GetGoogleFormOrderStatsAsync(cancellationToken)
                .ConfigureAwait(false);

            response = response with
            {
                IngestedOrdersCount = Math.Max(total, response.IngestedOrdersCount),
                PaidOrdersCount = paid,
                PendingOrdersCount = pending
            };
        }

        return Result.Success(response);
    }
}
