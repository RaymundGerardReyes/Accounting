using Rge.BuildingBlocks.Application.Messaging;

namespace Rge.Finance.Contracts.Events;

/// <summary>
/// Cross-module integration event published when an advance is reconciled, fully audited, and closed.
/// </summary>
public sealed record AdvanceClosedIntegrationEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string AdvanceId,
    string AdvanceNumber,
    string EmployeeId,
    long ReleasedAmountMinor,
    long ApprovedExpenseMinor,
    long CashReturnedMinor,
    long ReimbursementMinor,
    long FinalOutstandingMinor,
    string ControllerId,
    string ClosureReason) : IIntegrationEvent
{
    public const string Type = "finance.advance_closed";

    public static AdvanceClosedIntegrationEvent Create(
        string advanceId,
        string advanceNumber,
        string employeeId,
        long releasedAmountMinor,
        long approvedExpenseMinor,
        long cashReturnedMinor,
        long reimbursementMinor,
        long finalOutstandingMinor,
        string controllerId,
        string closureReason,
        DateTimeOffset closedAtUtc)
    {
        return new AdvanceClosedIntegrationEvent(
            EventId: Guid.NewGuid(),
            EventType: Type,
            OccurredAtUtc: closedAtUtc,
            CorrelationId: advanceId,
            AdvanceId: advanceId,
            AdvanceNumber: advanceNumber,
            EmployeeId: employeeId,
            ReleasedAmountMinor: releasedAmountMinor,
            ApprovedExpenseMinor: approvedExpenseMinor,
            CashReturnedMinor: cashReturnedMinor,
            ReimbursementMinor: reimbursementMinor,
            FinalOutstandingMinor: finalOutstandingMinor,
            ControllerId: controllerId,
            ClosureReason: closureReason);
    }
}
