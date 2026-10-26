using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExpenseReports.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.ExpenseReports.AddLines;

public sealed record AddExpenseReportLineItem(
    string TransactionDate,
    string ExpenseCategory,
    string BusinessPurpose,
    string MerchantOrPayee,
    long ClaimedAmountMinor,
    string? SchoolOrDestination = null,
    string? ReceiptOrInvoiceNo = null,
    bool OutOfBase = false,
    string? ReceiptDocumentId = null,
    string? MfarDocumentId = null,
    string? TaxCode = null,
    long? VatAmountMinor = null);

public sealed record AddExpenseReportLinesCommand(
    string ReportId,
    IReadOnlyList<AddExpenseReportLineItem> Lines) : ICommand<ExpenseReportDto>;

public sealed class AddLinesHandler : ICommandHandler<AddExpenseReportLinesCommand, ExpenseReportDto>
{
    private readonly IExpenseReportRepository _repository;
    private readonly IClock _clock;

    public AddLinesHandler(IExpenseReportRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<ExpenseReportDto>> HandleAsync(
        AddExpenseReportLinesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ReportId))
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.Validation("MissingReportId", "Report ID is required."));
        }

        if (command.Lines is null || command.Lines.Count == 0)
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.Validation("EmptyLines", "At least one expense line must be provided."));
        }

        ExpenseReport? report = await _repository.GetByIdAsync(command.ReportId.Trim(), cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.ReportNotFound(command.ReportId));
        }

        if (report.ReviewStatus is not (ExpenseReportStatus.Draft or ExpenseReportStatus.ReturnedForCorrection))
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.InvalidTransition("ExpenseReport", report.ReviewStatus.ToString(), "AddLines"));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;

        foreach (AddExpenseReportLineItem item in command.Lines)
        {
            var claimed = new Money(item.ClaimedAmountMinor, report.Currency);
            Money? vat = item.VatAmountMinor.HasValue ? new Money(item.VatAmountMinor.Value, report.Currency) : null;

            Result<ExpenseLine> lineResult = ExpenseLine.Create(
                expenseReportId: report.Id,
                employeeId: report.EmployeeId,
                transactionDate: item.TransactionDate,
                schoolOrDestination: item.SchoolOrDestination,
                expenseCategory: item.ExpenseCategory,
                businessPurpose: item.BusinessPurpose,
                merchantOrPayee: item.MerchantOrPayee,
                receiptOrInvoiceNo: item.ReceiptOrInvoiceNo,
                claimedAmount: claimed,
                outOfBase: item.OutOfBase,
                receiptDocumentId: item.ReceiptDocumentId,
                mfarDocumentId: item.MfarDocumentId,
                taxCode: item.TaxCode,
                vatAmount: vat,
                duplicateWarning: false,
                duplicateOfLineId: null,
                nowUtc: nowUtc);

            if (lineResult.IsFailure)
            {
                return Result.Failure<ExpenseReportDto>(lineResult.Error);
            }

            report.AddLine(lineResult.Value);
        }

        await _repository.SaveAsync(report, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateHandler.MapToDto(report));
    }
}
