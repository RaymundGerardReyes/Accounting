using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.ExpenseReports.Create;

public sealed record CreateExpenseReportCommand(
    string EmployeeId,
    string DepartmentId,
    string CostCenterId,
    string ReportingPeriod,
    string BusinessPurposeSummary,
    bool OutOfBaseActivity = false,
    string? ConsolidatedReceiptDocId = null,
    string? MfarDocId = null,
    bool EmployeeDeclarationConfirmed = true,
    string? Remarks = null,
    string Currency = "PHP",
    string? IdempotencyKey = null) : ICommand<ExpenseReportDto>;

public sealed record ExpenseReportDto(
    string Id,
    string ReportNumber,
    string EmployeeId,
    string DepartmentId,
    string CostCenterId,
    string ReportingPeriod,
    string BusinessPurposeSummary,
    bool OutOfBaseActivity,
    long TotalClaimedAmountMinor,
    long TotalApprovedAmountMinor,
    string Currency,
    string ReviewStatus,
    string SettlementStatus,
    string? ReviewerId,
    IReadOnlyList<ExpenseLineDto> Lines,
    DateTimeOffset CreatedAtUtc);

public sealed record ExpenseLineDto(
    string Id,
    string TransactionDate,
    string ExpenseCategory,
    string BusinessPurpose,
    string MerchantOrPayee,
    string? ReceiptOrInvoiceNo,
    long ClaimedAmountMinor,
    long? ApprovedAmountMinor,
    string Status);

public sealed class CreateHandler : ICommandHandler<CreateExpenseReportCommand, ExpenseReportDto>
{
    private readonly IExpenseReportRepository _repository;
    private readonly IClock _clock;

    public CreateHandler(IExpenseReportRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<ExpenseReportDto>> HandleAsync(
        CreateExpenseReportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;
        long seq = await _repository.NextReportSequenceAsync(cancellationToken).ConfigureAwait(false);
        string reportNo = $"EXP-{command.ReportingPeriod.Replace("-", string.Empty, StringComparison.Ordinal)}-{seq:D4}";

        Result<ExpenseReport> createResult = ExpenseReport.Create(
            reportNumber: reportNo,
            employeeId: command.EmployeeId,
            departmentId: command.DepartmentId,
            costCenterId: command.CostCenterId,
            reportingPeriod: command.ReportingPeriod,
            businessPurposeSummary: command.BusinessPurposeSummary,
            outOfBaseActivity: command.OutOfBaseActivity,
            consolidatedReceiptDocId: command.ConsolidatedReceiptDocId,
            mfarDocId: command.MfarDocId,
            employeeDeclarationConfirmed: command.EmployeeDeclarationConfirmed,
            remarks: command.Remarks,
            currency: command.Currency,
            idempotencyKey: command.IdempotencyKey,
            nowUtc: nowUtc);

        if (createResult.IsFailure)
        {
            return Result.Failure<ExpenseReportDto>(createResult.Error);
        }

        await _repository.SaveAsync(createResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(createResult.Value));
    }

    public static ExpenseReportDto MapToDto(ExpenseReport r)
    {
        ArgumentNullException.ThrowIfNull(r);

        var lines = r.Lines.Select(l => new ExpenseLineDto(
            Id: l.Id,
            TransactionDate: l.TransactionDate,
            ExpenseCategory: l.ExpenseCategory,
            BusinessPurpose: l.BusinessPurpose,
            MerchantOrPayee: l.MerchantOrPayee,
            ReceiptOrInvoiceNo: l.ReceiptOrInvoiceNo,
            ClaimedAmountMinor: l.ClaimedAmount.AmountMinor,
            ApprovedAmountMinor: l.ApprovedAmount?.AmountMinor,
            Status: l.LineStatus.ToString().ToLowerInvariant())).ToList();

        return new ExpenseReportDto(
            Id: r.Id,
            ReportNumber: r.ReportNumber,
            EmployeeId: r.EmployeeId,
            DepartmentId: r.DepartmentId,
            CostCenterId: r.CostCenterId,
            ReportingPeriod: r.ReportingPeriod,
            BusinessPurposeSummary: r.BusinessPurposeSummary,
            OutOfBaseActivity: r.OutOfBaseActivity,
            TotalClaimedAmountMinor: r.CalculateTotalClaimed().AmountMinor,
            TotalApprovedAmountMinor: r.CalculateTotalApproved().AmountMinor,
            Currency: r.Currency,
            ReviewStatus: r.ReviewStatus.ToString().ToLowerInvariant(),
            SettlementStatus: r.SettlementStatus.ToString().ToLowerInvariant(),
            ReviewerId: r.ReviewerId,
            Lines: lines,
            CreatedAtUtc: r.CreatedAtUtc);
    }
}
