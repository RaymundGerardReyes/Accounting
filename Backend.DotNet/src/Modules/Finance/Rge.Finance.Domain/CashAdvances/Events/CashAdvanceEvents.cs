using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.CashAdvances.Events;

public sealed record CashAdvanceCreated(
    string AdvanceId,
    string AdvanceNumber,
    string RequestorId,
    string EmployeeId,
    long RequestedAmountMinor,
    string Currency,
    DateTimeOffset CreatedAtUtc) : DomainEvent;

public sealed record CashAdvanceSubmitted(
    string AdvanceId,
    DateTimeOffset SubmittedAtUtc) : DomainEvent;

public sealed record CashAdvanceApproved(
    string AdvanceId,
    string ApproverId,
    long ApprovedAmountMinor,
    DateTimeOffset ApprovedAtUtc) : DomainEvent;

public sealed record CashAdvanceRejected(
    string AdvanceId,
    string ApproverId,
    string Reason,
    DateTimeOffset DecidedAtUtc) : DomainEvent;

public sealed record CashAdvanceReturnedForCorrection(
    string AdvanceId,
    string ApproverId,
    string Reason,
    DateTimeOffset DecidedAtUtc) : DomainEvent;

public sealed record CashAdvanceCancelled(
    string AdvanceId,
    string ActorId,
    string Reason,
    DateTimeOffset CancelledAtUtc) : DomainEvent;

public sealed record CashAdvanceDisbursed(
    string AdvanceId,
    string DisbursementId,
    long DisbursedAmountMinor,
    DateTimeOffset DisbursedAtUtc) : DomainEvent;

public sealed record CashAdvanceClosed(
    string AdvanceId,
    string ControllerId,
    string Reason,
    DateTimeOffset ClosedAtUtc) : DomainEvent;
