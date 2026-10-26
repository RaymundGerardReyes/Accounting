using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Application.GetFormStatus;

public sealed record GetGoogleFormsStatusQuery : IQuery<GoogleFormsStatusResponse>;

public sealed class GetFormStatusHandler : IQueryHandler<GetGoogleFormsStatusQuery, GoogleFormsStatusResponse>
{
    private readonly IGoogleFormsSubmissionStore _store;

    public GetFormStatusHandler(IGoogleFormsSubmissionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<Result<GoogleFormsStatusResponse>> HandleAsync(
        GetGoogleFormsStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        GoogleFormsStatusResponse response = await _store
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

