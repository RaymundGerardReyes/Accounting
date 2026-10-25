using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Approvals;

/// <summary>
/// Immutable, append-only record tracking an approval decision on a financial entity (CashAdvance, Disbursement, PurchaseRequest).
/// Matches the approval_history schema from the authoritative specification.
/// </summary>
public sealed class ApprovalHistoryEntry : Entity<string>
{
    public string EntityType { get; }
    public string EntityId { get; }
    public string ApproverId { get; }
    public string ApproverRole { get; }
    public string ApprovalStage { get; }
    public string Decision { get; }
    public long? ApprovedAmountMinor { get; }
    public string Comments { get; }
    public string PreviousStatus { get; }
    public string ResultingStatus { get; }
    public DateTimeOffset DecidedAt { get; }

    public ApprovalHistoryEntry(
        string id,
        string entityType,
        string entityId,
        string approverId,
        string approverRole,
        string approvalStage,
        string decision,
        long? approvedAmountMinor,
        string comments,
        string previousStatus,
        string resultingStatus,
        DateTimeOffset decidedAt) : base(id)
    {
        EntityType = Guard.AgainstNullOrWhiteSpace(entityType);
        EntityId = Guard.AgainstNullOrWhiteSpace(entityId);
        ApproverId = Guard.AgainstNullOrWhiteSpace(approverId);
        ApproverRole = Guard.AgainstNullOrWhiteSpace(approverRole);
        ApprovalStage = Guard.AgainstNullOrWhiteSpace(approvalStage);
        Decision = Guard.AgainstNullOrWhiteSpace(decision);
        ApprovedAmountMinor = approvedAmountMinor;
        Comments = comments ?? string.Empty;
        PreviousStatus = Guard.AgainstNullOrWhiteSpace(previousStatus);
        ResultingStatus = Guard.AgainstNullOrWhiteSpace(resultingStatus);
        DecidedAt = decidedAt;
    }

    public static ApprovalHistoryEntry Create(
        string entityType,
        string entityId,
        string approverId,
        string approverRole,
        string approvalStage,
        string decision,
        string previousStatus,
        string resultingStatus,
        string comments,
        long? approvedAmountMinor = null,
        DateTimeOffset? decidedAt = null,
        string? id = null)
    {
        return new ApprovalHistoryEntry(
            id: id ?? Guid.NewGuid().ToString("D"),
            entityType: entityType,
            entityId: entityId,
            approverId: approverId,
            approverRole: approverRole,
            approvalStage: approvalStage,
            decision: decision,
            approvedAmountMinor: approvedAmountMinor,
            comments: comments,
            previousStatus: previousStatus,
            resultingStatus: resultingStatus,
            decidedAt: decidedAt ?? DateTimeOffset.UtcNow);
    }
}

