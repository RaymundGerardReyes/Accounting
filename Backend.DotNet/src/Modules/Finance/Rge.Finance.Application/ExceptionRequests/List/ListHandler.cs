using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExceptionRequests.Submit;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.ExceptionRequests.List;

public sealed record ListExceptionRequestsQuery(
    string? EmployeeId = null,
    string? Status = null,
    int Limit = 50) : IQuery<IReadOnlyList<ExceptionRequestDto>>;

public sealed class ListHandler : IQueryHandler<ListExceptionRequestsQuery, IReadOnlyList<ExceptionRequestDto>>
{
    private readonly IExceptionRequestRepository _repository;

    public ListHandler(IExceptionRequestRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ExceptionRequestDto>>> HandleAsync(
        ListExceptionRequestsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 200);
        IReadOnlyList<ExceptionRequest> requests = await _repository.ListAsync(query.EmployeeId, query.Status, limit, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ExceptionRequestDto> dtos = requests.Select(SubmitHandler.MapToDto).ToList();
        return Result.Success(dtos);
    }
}
