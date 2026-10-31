using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Exceptions;

public enum ExceptionRegisterStatus
{
    Open,
    UnderReview,
    Resolved,
    Reopened
}

/// <summary>
/// Operational control item tracked in the Finance Exception Register.
/// </summary>
public sealed class ExceptionRegisterItem : Entity<string>
{
    public string RegisterNumber { get; private set; }
    public string EntityType { get; private set; }
    public string EntityId { get; private set; }
    public string ExceptionCategory { get; private set; }
    public string Severity { get; private set; }
    public string? OwnerId { get; private set; }
    public string? DepartmentId { get; private set; }
    public Money Amount { get; private set; }
    public string Reason { get; private set; }
    public string? EvidenceDocId { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public ExceptionRegisterStatus Status { get; private set; }
    public string? ReviewerId { get; private set; }
    public DateTimeOffset DetectedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public ExceptionRegisterItem(
        string id,
        string registerNumber,
        string entityType,
        string entityId,
        string exceptionCategory,
        string severity,
        string? ownerId,
        string? departmentId,
        Money amount,
        string reason,
        string? evidenceDocId,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        RegisterNumber = Guard.AgainstNullOrWhiteSpace(registerNumber);
        EntityType = Guard.AgainstNullOrWhiteSpace(entityType);
        EntityId = Guard.AgainstNullOrWhiteSpace(entityId);
        ExceptionCategory = Guard.AgainstNullOrWhiteSpace(exceptionCategory);
        Severity = Guard.AgainstNullOrWhiteSpace(severity);
        OwnerId = ownerId;
        DepartmentId = departmentId;
        Amount = amount;
        Reason = Guard.AgainstNullOrWhiteSpace(reason);
        EvidenceDocId = evidenceDocId;
        Status = ExceptionRegisterStatus.Open;
        DetectedAtUtc = nowUtc;
    }

    public void Resolve(string reviewerId, string resolutionNotes, DateTimeOffset nowUtc)
    {
        ReviewerId = Guard.AgainstNullOrWhiteSpace(reviewerId);
        ResolutionNotes = Guard.AgainstNullOrWhiteSpace(resolutionNotes);
        Status = ExceptionRegisterStatus.Resolved;
        ResolvedAtUtc = nowUtc;
    }

    public void Reopen(string reviewerId, string reason)
    {
        ReviewerId = Guard.AgainstNullOrWhiteSpace(reviewerId);
        Reason = Guard.AgainstNullOrWhiteSpace(reason);
        Status = ExceptionRegisterStatus.Reopened;
        ResolvedAtUtc = null;
    }
}
