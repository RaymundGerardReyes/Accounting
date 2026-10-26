using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Application.IntegrationStatusProbe;

public sealed record GetIntegrationStatusQuery : IQuery<IntegrationStatusResponse>;

public sealed class IntegrationStatusProbeHandler : IQueryHandler<GetIntegrationStatusQuery, IntegrationStatusResponse>
{
    private readonly IIntegrationDiagnosticProbe _probe;

    public IntegrationStatusProbeHandler(IIntegrationDiagnosticProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public async Task<Result<IntegrationStatusResponse>> HandleAsync(
        GetIntegrationStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        IntegrationStatusResponse response = await _probe
            .ProbeAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

