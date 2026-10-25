using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;
using Rge.Payments.Application.Reconciliation.ReconcileOne;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.Reconciliation.ReconcileAllNonTerminal;

public sealed class ReconcileAllNonTerminalHandler : ICommandHandler<ReconcileAllNonTerminalCommand, ReconcileAllNonTerminalResponse>
{
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly ReconcileOneHandler _reconcileOneHandler;

    public ReconcileAllNonTerminalHandler(
        IPaymentAttemptRepository attemptRepository,
        ReconcileOneHandler reconcileOneHandler)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(reconcileOneHandler);

        _attemptRepository = attemptRepository;
        _reconcileOneHandler = reconcileOneHandler;
    }

    public async Task<Result<ReconcileAllNonTerminalResponse>> HandleAsync(
        ReconcileAllNonTerminalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<PaymentAttempt> nonTerminal = await _attemptRepository
            .ListNonTerminalAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ReconcileOneResponse> details = new(nonTerminal.Count);
        foreach (PaymentAttempt attempt in nonTerminal)
        {
            var res = await _reconcileOneHandler
                .HandleAsync(new ReconcileOneCommand(attempt.Id), cancellationToken)
                .ConfigureAwait(false);

            if (res.IsSuccess)
            {
                details.Add(res.Value);
            }
        }

        return Result.Success(new ReconcileAllNonTerminalResponse(
            ReconciledCount: details.Count,
            Details: details));
    }
}
