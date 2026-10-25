using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Audit.Contracts;

namespace Rge.Audit.Application.AppendAuditEvent;

public sealed record AppendAuditEventCommand(AuditTrailEntry Entry) : ICommand<AuditRecordResult>;

public sealed class AppendAuditEventHandler : ICommandHandler<AppendAuditEventCommand, AuditRecordResult>
{
    private readonly IAuditTrail _auditTrail;

    public AppendAuditEventHandler(IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(auditTrail);
        _auditTrail = auditTrail;
    }

    public async Task<Result<AuditRecordResult>> HandleAsync(
        AppendAuditEventCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        AuditRecordResult recorded = await _auditTrail.RecordAsync(command.Entry, cancellationToken).ConfigureAwait(false);
        return Result.Success(recorded);
    }
}

