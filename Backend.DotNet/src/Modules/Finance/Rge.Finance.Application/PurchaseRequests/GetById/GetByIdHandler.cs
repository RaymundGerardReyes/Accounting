using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.PurchaseRequests.Create;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.PurchaseRequests.GetById;

public sealed record GetPurchaseRequestByIdQuery(string Id) : IQuery<PurchaseRequestDto>;

public sealed class GetByIdHandler : IQueryHandler<GetPurchaseRequestByIdQuery, PurchaseRequestDto>
{
    private readonly IPurchaseRequestRepository _repository;

    public GetByIdHandler(IPurchaseRequestRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<PurchaseRequestDto>> HandleAsync(
        GetPurchaseRequestByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Id))
        {
            return Result.Failure<PurchaseRequestDto>(FinanceErrors.Validation("MissingPrId", "Purchase Request ID is required."));
        }

        PurchaseRequest? pr = await _repository.GetByIdAsync(query.Id.Trim(), cancellationToken).ConfigureAwait(false);
        if (pr is null)
        {
            return Result.Failure<PurchaseRequestDto>(FinanceErrors.Validation("PrNotFound", $"Purchase Request '{query.Id}' was not found."));
        }

        return Result.Success(CreateHandler.MapToDto(pr));
    }
}
