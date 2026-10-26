using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.Policies.AuthorVersion;

public sealed record AuthorPolicyVersionCommand(
    string PolicyCode,
    string AuthorId,
    long VersionNumber,
    string EffectiveFrom,
    long AggregateMonthlyLimitMinor,
    string Currency,
    int MaxReportsPerPeriod,
    string ChangeJustification,
    string? EffectiveUntil = null,
    string PeriodType = "calendar_month",
    int SubmissionDeadlineDay = 2,
    string DeadlineInterpretation = "strict_next_business_day",
    int FinanceSlaDays = 5,
    string SlaDayType = "business_days",
    bool RequireConsolidatedReceiptPdf = true,
    bool RequireMfarForOutOfBase = true,
    bool AllowPartialLineApproval = true,
    bool BlockNewAdvanceWhenOverdue = true,
    long? MaxAdvanceAmountMinor = null,
    string EligibleCategoriesJson = "[\"travel\", \"supplies\", \"meals\", \"transportation\"]") : ICommand<PolicyVersionDto>;

public sealed record PolicyVersionDto(
    string Id,
    string PolicyId,
    long VersionNumber,
    string Status,
    string EffectiveFrom,
    string? EffectiveUntil,
    long AggregateMonthlyLimitMinor,
    string Currency,
    int MaxReportsPerPeriod,
    string AuthorId,
    string? ApproverId,
    DateTimeOffset CreatedAtUtc);

public sealed class AuthorVersionHandler : ICommandHandler<AuthorPolicyVersionCommand, PolicyVersionDto>
{
    private readonly IPolicyRepository _repository;
    private readonly IClock _clock;

    public AuthorVersionHandler(IPolicyRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PolicyVersionDto>> HandleAsync(
        AuthorPolicyVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        FinancialPolicy? policy = await _repository.GetByCodeAsync(command.PolicyCode, cancellationToken).ConfigureAwait(false);
        if (policy is null)
        {
            return Result.Failure<PolicyVersionDto>(Error.NotFound(
                "Finance.Policy.NotFound",
                $"Policy with code '{command.PolicyCode}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        var limit = new Money(command.AggregateMonthlyLimitMinor, command.Currency);
        Money? maxAdv = command.MaxAdvanceAmountMinor.HasValue ? new Money(command.MaxAdvanceAmountMinor.Value, command.Currency) : null;

        var version = new PolicyVersion(
            id: Guid.NewGuid().ToString("D"),
            policyId: policy.Id,
            versionNumber: command.VersionNumber,
            effectiveFrom: command.EffectiveFrom,
            effectiveUntil: command.EffectiveUntil,
            aggregateMonthlyLimit: limit,
            maxReportsPerPeriod: command.MaxReportsPerPeriod,
            periodType: command.PeriodType,
            submissionDeadlineDay: command.SubmissionDeadlineDay,
            deadlineInterpretation: command.DeadlineInterpretation,
            financeSlaDays: command.FinanceSlaDays,
            slaDayType: command.SlaDayType,
            requireConsolidatedReceiptPdf: command.RequireConsolidatedReceiptPdf,
            requireMfarForOutOfBase: command.RequireMfarForOutOfBase,
            allowPartialLineApproval: command.AllowPartialLineApproval,
            blockNewAdvanceWhenOverdue: command.BlockNewAdvanceWhenOverdue,
            maxAdvanceAmount: maxAdv,
            eligibleCategoriesJson: command.EligibleCategoriesJson,
            previousValuesJson: null,
            changeJustification: command.ChangeJustification,
            authorId: command.AuthorId,
            nowUtc: nowUtc);

        await _repository.SaveVersionAsync(version, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(version));
    }

    public static PolicyVersionDto MapToDto(PolicyVersion v)
    {
        ArgumentNullException.ThrowIfNull(v);

        return new PolicyVersionDto(
            Id: v.Id,
            PolicyId: v.PolicyId,
            VersionNumber: v.VersionNumber,
            Status: v.Status.ToString().ToLowerInvariant(),
            EffectiveFrom: v.EffectiveFrom,
            EffectiveUntil: v.EffectiveUntil,
            AggregateMonthlyLimitMinor: v.AggregateMonthlyLimit.AmountMinor,
            Currency: v.Currency,
            MaxReportsPerPeriod: v.MaxReportsPerPeriod,
            AuthorId: v.AuthorId,
            ApproverId: v.ApproverId,
            CreatedAtUtc: v.CreatedAtUtc);
    }
}
