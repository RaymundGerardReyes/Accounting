using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Reconciliation.ReconcileOne;

namespace Rge.Payments.Application.Reconciliation.ReconcileAllNonTerminal;

public sealed record ReconcileAllNonTerminalCommand : ICommand<ReconcileAllNonTerminalResponse>;

public sealed record ReconcileAllNonTerminalResponse(
    int ReconciledCount,
    IReadOnlyList<ReconcileOneResponse> Details);
