using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Policy;

public enum ExceptionRequestStatus
{
    Draft,
    Submitted,
    UnderReview,
    Approved,
    Rejected,
    Expired,
    Consumed
}

/// <summary>
/// Aggregate root representing an employee exception request for exceeding policy limits or deadlines.
/// </summary>
public sealed class ExceptionRequest : AggregateRoot<string>
{
    public string ExceptionNumber { get; private set; }
    public string EmployeeId { get; private set; }
    public string RequesterId { get; private set; }
    public string TargetEntityType { get; private set; }
    public string? TargetEntityId { get; private set; }
    public string ReportingPeriod { get; private set; }
    public string PolicyVersionId { get; private set; }
    public string ViolatedRuleCode { get; private set; }
    public string ExceptionType { get; private set; }
    public Money RequestedAmount { get; private set; }
    public Money? ApprovedMaxAmount { get; private set; }
    public string BusinessJustification { get; private set; }
    public IReadOnlyList<string> SupportingDocumentIds { get; private set; }
    public ExceptionRequestStatus Status { get; private set; }

    public string? AssignedReviewerId { get; private set; }
    public string? ApproverId { get; private set; }
    public string? DecisionReason { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public string? ConsumedByEntityId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public ExceptionRequest(
        string id,
        string exceptionNumber,
        string employeeId,
        string requesterId,
        string targetEntityType,
        string? targetEntityId,
        string reportingPeriod,
        string policyVersionId,
        string violatedRuleCode,
        string exceptionType,
        Money requestedAmount,
        string businessJustification,
        IReadOnlyList<string> supportingDocumentIds,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        ExceptionNumber = Guard.AgainstNullOrWhiteSpace(exceptionNumber);
        EmployeeId = Guard.AgainstNullOrWhiteSpace(employeeId);
        RequesterId = Guard.AgainstNullOrWhiteSpace(requesterId);
        TargetEntityType = Guard.AgainstNullOrWhiteSpace(targetEntityType);
        TargetEntityId = targetEntityId;
        ReportingPeriod = Guard.AgainstNullOrWhiteSpace(reportingPeriod);
        PolicyVersionId = Guard.AgainstNullOrWhiteSpace(policyVersionId);
        ViolatedRuleCode = Guard.AgainstNullOrWhiteSpace(violatedRuleCode);
        ExceptionType = Guard.AgainstNullOrWhiteSpace(exceptionType);
        RequestedAmount = requestedAmount;
        BusinessJustification = Guard.AgainstNullOrWhiteSpace(businessJustification);
        SupportingDocumentIds = supportingDocumentIds ?? Array.Empty<string>();

        Status = ExceptionRequestStatus.Draft;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<ExceptionRequest> Create(
        string exceptionNumber,
        string employeeId,
        string requesterId,
        string targetEntityType,
        string? targetEntityId,
        string reportingPeriod,
        string policyVersionId,
        string violatedRuleCode,
        string exceptionType,
        Money requestedAmount,
        string businessJustification,
        IReadOnlyList<string>? supportingDocumentIds,
        DateTimeOffset nowUtc)
    {
        var request = new ExceptionRequest(
            id: Guid.NewGuid().ToString("D"),
            exceptionNumber: exceptionNumber,
            employeeId: employeeId,
            requesterId: requesterId,
            targetEntityType: targetEntityType,
            targetEntityId: targetEntityId,
            reportingPeriod: reportingPeriod,
            policyVersionId: policyVersionId,
            violatedRuleCode: violatedRuleCode,
            exceptionType: exceptionType,
            requestedAmount: requestedAmount,
            businessJustification: businessJustification,
            supportingDocumentIds: supportingDocumentIds ?? Array.Empty<string>(),
            nowUtc: nowUtc);

        return Result.Success(request);
    }

    public Result Submit(DateTimeOffset nowUtc)
    {
        if (Status != ExceptionRequestStatus.Draft)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("ExceptionRequest", Status.ToString(), nameof(ExceptionRequestStatus.Submitted)));
        }

        Status = ExceptionRequestStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Decide(string approverId, bool approved, string? reason, Money? approvedAmount, DateTimeOffset? expiresAt, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        if (string.Equals(approverId, RequesterId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FinanceErrors.SodViolation("INV-SOD-001", "Requester cannot approve their own exception request."));
        }

        ApproverId = approverId;
        DecisionReason = reason;
        DecidedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        if (approved)
        {
            Status = ExceptionRequestStatus.Approved;
            ApprovedMaxAmount = approvedAmount ?? RequestedAmount;
            ExpiresAtUtc = expiresAt ?? nowUtc.AddDays(30);
        }
        else
        {
            Status = ExceptionRequestStatus.Rejected;
        }

        return Result.Success();
    }

    public Result Consume(string consumedByEntityId, DateTimeOffset nowUtc)
    {
        if (Status != ExceptionRequestStatus.Approved)
        {
            return Result.Failure(FinanceErrors.Validation("ExceptionNotApproved", "Only approved exceptions can be consumed."));
        }

        if (ExpiresAtUtc.HasValue && nowUtc > ExpiresAtUtc.Value)
        {
            Status = ExceptionRequestStatus.Expired;
            UpdatedAtUtc = nowUtc;
            return Result.Failure(FinanceErrors.Validation("ExceptionExpired", "This exception request has expired."));
        }

        Status = ExceptionRequestStatus.Consumed;
        ConsumedAtUtc = nowUtc;
        ConsumedByEntityId = Guard.AgainstNullOrWhiteSpace(consumedByEntityId);
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }
}
