using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.SecurityGuards;
using Rge.Finance.Contracts;
using Rge.Finance.Infrastructure.Calendar;
using Rge.Finance.Infrastructure.Persistence.Repositories;
using Rge.Finance.Infrastructure.Policy;
using Rge.Finance.Infrastructure.Reporting;

namespace Rge.Finance.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Core infrastructure singletons
        services.AddSingleton<PolicyCache>();
        services.AddSingleton<PolicyCacheInvalidator>();
        services.AddScoped<PhHolidayCalendarProvider>();
        services.AddSingleton<ISodGuard, SodGuard>();

        // Domain repositories
        services.AddScoped<ICashAdvanceRepository, CashAdvanceRepository>();
        services.AddScoped<IDisbursementRepository, DisbursementRepository>();
        services.AddScoped<IExpenseReportRepository, ExpenseReportRepository>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<IExceptionRequestRepository, ExceptionRequestRepository>();
        services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();
        services.AddScoped<IAccountPayableRepository, AccountPayableRepository>();
        services.AddScoped<IPaymentRecordRepository, PaymentRecordRepository>();
        services.AddScoped<IVendorRepository, VendorRepository>();
        services.AddScoped<ITaxCodeRepository, TaxCodeRepository>();
        services.AddScoped<IExceptionRegisterRepository, ExceptionRegisterRepository>();
        services.AddScoped<IAdvanceLedgerStore, AdvanceLedgerStore>();
        services.AddScoped<ISettlementApplicationStore, SettlementApplicationStore>();
        services.AddScoped<IAdvanceBalanceQuery, AdvanceBalanceQuery>();

        // Cash Advance handlers
        services.AddTransient<Application.CashAdvances.Create.CreateCashAdvanceHandler>();
        services.AddTransient<Application.CashAdvances.GetById.GetByIdHandler>();
        services.AddTransient<Application.CashAdvances.List.ListHandler>();
        services.AddTransient<Application.CashAdvances.Submit.SubmitHandler>();
        services.AddTransient<Application.CashAdvances.Decide.DecideHandler>();
        services.AddTransient<Application.CashAdvances.CashReturn.CashReturnHandler>();
        services.AddTransient<Application.CashAdvances.ReimbursementSettle.ReimbursementSettleHandler>();
        services.AddTransient<Application.CashAdvances.Close.CloseHandler>();

        // Disbursement handlers
        services.AddTransient<Application.Disbursements.Prepare.PrepareHandler>();
        services.AddTransient<Application.Disbursements.Verify.VerifyHandler>();
        services.AddTransient<Application.Disbursements.BankApprove.BankApproveHandler>();
        services.AddTransient<Application.Disbursements.Execute.ExecuteHandler>();
        services.AddTransient<Application.Disbursements.VerifyEvidence.VerifyEvidenceHandler>();
        services.AddTransient<Application.Disbursements.SetExceptionState.SetExceptionStateHandler>();

        // Expense Report handlers
        services.AddTransient<Application.ExpenseReports.Create.CreateHandler>();
        services.AddTransient<Application.ExpenseReports.List.ListHandler>();
        services.AddTransient<Application.ExpenseReports.GetById.GetByIdHandler>();
        services.AddTransient<Application.ExpenseReports.Patch.PatchHandler>();
        services.AddTransient<Application.ExpenseReports.AddLines.AddLinesHandler>();
        services.AddTransient<Application.ExpenseReports.Evaluate.EvaluateHandler>();
        services.AddTransient<Application.ExpenseReports.Submit.SubmitHandler>();
        services.AddTransient<Application.ExpenseReports.Review.ReviewHandler>();

        // Purchase Request handlers
        services.AddTransient<Application.PurchaseRequests.Create.CreateHandler>();
        services.AddTransient<Application.PurchaseRequests.List.ListHandler>();
        services.AddTransient<Application.PurchaseRequests.GetById.GetByIdHandler>();
        services.AddTransient<Application.PurchaseRequests.Submit.SubmitHandler>();
        services.AddTransient<Application.PurchaseRequests.Decide.DecideHandler>();

        // Payable handlers
        services.AddTransient<Application.Payables.Register.RegisterHandler>();
        services.AddTransient<Application.Payables.PreparePayment.PreparePaymentHandler>();
        services.AddTransient<Application.Payables.VerifyPayment.VerifyPaymentHandler>();
        services.AddTransient<Application.Payables.BankApprovePayment.BankApprovePaymentHandler>();
        services.AddTransient<Application.Payables.ExecutePayment.ExecutePaymentHandler>();
        services.AddTransient<Application.Payables.ReconcilePayment.ReconcilePaymentHandler>();
        services.AddTransient<Application.Payables.SetPaymentExceptionState.SetPaymentExceptionStateHandler>();

        // Policy handlers
        services.AddTransient<Application.Policies.List.ListHandler>();
        services.AddTransient<Application.Policies.AuthorVersion.AuthorVersionHandler>();
        services.AddTransient<Application.Policies.SubmitVersion.SubmitVersionHandler>();
        services.AddTransient<Application.Policies.ApproveVersion.ApproveVersionHandler>();
        services.AddTransient<Application.Policies.RetireVersion.RetireVersionHandler>();

        // Exception Request handlers
        services.AddTransient<Application.ExceptionRequests.List.ListHandler>();
        services.AddTransient<Application.ExceptionRequests.Submit.SubmitHandler>();
        services.AddTransient<Application.ExceptionRequests.Decide.DecideHandler>();

        // Tax Code handlers
        services.AddTransient<Application.TaxCodes.List.ListHandler>();
        services.AddTransient<Application.TaxCodes.Register.RegisterHandler>();

        // Vendor handlers
        services.AddTransient<Application.Vendors.Register.RegisterHandler>();
        services.AddTransient<Application.Vendors.List.ListHandler>();
        services.AddTransient<Application.Vendors.Verify.VerifyHandler>();
        services.AddTransient<Application.Vendors.RequestBankChange.RequestBankChangeHandler>();
        services.AddTransient<Application.Vendors.DecideBankChange.DecideBankChangeHandler>();

        // Reporting handlers & read services
        services.AddTransient<Application.Reporting.KpiDefinitions.KpiDefinitionsHandler>();
        services.AddTransient<Application.Reporting.BusinessConfirmations.BusinessConfirmationsHandler>();
        services.AddTransient<Application.Reporting.DashboardSummary.DashboardSummaryHandler>();
        services.AddTransient<Application.Reporting.ExceptionRegister.ExceptionRegisterHandler>();
        services.AddTransient<Application.Reporting.Tracker.TrackerHandler>();
        services.AddTransient<Application.Reporting.TrackerCsvExport.TrackerCsvExportHandler>();
        services.AddTransient<Application.Reporting.AgingBuckets.AgingBucketsHandler>();
        services.AddTransient<DashboardReadService>();
        services.AddTransient<TrackerReadService>();

        return services;
    }
}
