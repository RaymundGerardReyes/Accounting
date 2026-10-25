using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Audit.Application.Ports;

namespace Rge.Audit.Application.VerifyChain;

public sealed record VerifyChainQuery : IQuery<ChainIntegrityDto>;

public sealed class VerifyChainHandler : IQueryHandler<VerifyChainQuery, ChainIntegrityDto>
{
    private readonly IChainVerifier _chainVerifier;

    public VerifyChainHandler(IChainVerifier chainVerifier)
    {
        ArgumentNullException.ThrowIfNull(chainVerifier);
        _chainVerifier = chainVerifier;
    }

    public async Task<Result<ChainIntegrityDto>> HandleAsync(
        VerifyChainQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ChainIntegrityDto result = await _chainVerifier.VerifyChainAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(result);
    }
}

