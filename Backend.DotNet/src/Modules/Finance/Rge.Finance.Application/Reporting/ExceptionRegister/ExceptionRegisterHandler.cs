using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Exceptions;

namespace Rge.Finance.Application.Reporting.ExceptionRegister;

public sealed record ListExceptionRegisterQuery(
    string? Status = null,
    int Limit = 50) : IQuery<IReadOnlyList<ExceptionRegisterItemDto>>;

public sealed record ResolveExceptionRegisterItemCommand(
    string RegisterId,
    string ResolverId,
    string ResolutionNotes) : ICommand<ExceptionRegisterItemDto>;

public sealed record ExceptionRegisterItemDto(
    string Id,
    string RegisterNumber,
    string EntityType,
    string EntityId,
    string ExceptionCategory,
    string Severity,
    string? OwnerId,
    string? DepartmentId,
    long AmountMinor,
    string Currency,
    string Reason,
    string Status,
    string? ReviewerId,
    string? ResolutionNotes,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public sealed class ExceptionRegisterHandler :
    IQueryHandler<ListExceptionRegisterQuery, IReadOnlyList<ExceptionRegisterItemDto>>,
    ICommandHandler<ResolveExceptionRegisterItemCommand, ExceptionRegisterItemDto>
{
    private readonly IExceptionRegisterRepository _repository;
    private readonly IClock _clock;

    public ExceptionRegisterHandler(IExceptionRegisterRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ExceptionRegisterItemDto>>> HandleAsync(
        ListExceptionRegisterQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 200);
        IReadOnlyList<ExceptionRegisterItem> items = await _repository.ListAsync(query.Status, limit, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ExceptionRegisterItemDto> dtos = items.Select(MapToDto).ToList();
        return Result.Success(dtos);
    }

    public async Task<Result<ExceptionRegisterItemDto>> HandleAsync(
        ResolveExceptionRegisterItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ExceptionRegisterItem? item = await _repository.GetByIdAsync(command.RegisterId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<ExceptionRegisterItemDto>(Error.NotFound(
                "Finance.ExceptionRegister.NotFound",
                $"Exception register item '{command.RegisterId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        item.Resolve(command.ResolverId, command.ResolutionNotes, nowUtc);

        await _repository.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(item));
    }

    public static ExceptionRegisterItemDto MapToDto(ExceptionRegisterItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new ExceptionRegisterItemDto(
            Id: item.Id,
            RegisterNumber: item.RegisterNumber,
            EntityType: item.EntityType,
            EntityId: item.EntityId,
            ExceptionCategory: item.ExceptionCategory,
            Severity: item.Severity,
            OwnerId: item.OwnerId,
            DepartmentId: item.DepartmentId,
            AmountMinor: item.Amount.AmountMinor,
            Currency: item.Amount.CurrencyCode,
            Reason: item.Reason,
            Status: item.Status.ToString().ToLowerInvariant(),
            ReviewerId: item.ReviewerId,
            ResolutionNotes: item.ResolutionNotes,
            DetectedAtUtc: item.DetectedAtUtc,
            ResolvedAtUtc: item.ResolvedAtUtc);
    }
}
