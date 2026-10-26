using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.Finance.Api.CashAdvances;
using Rge.Finance.Api.Disbursements;
using Rge.Finance.Api.Exceptions;
using Rge.Finance.Api.ExpenseReports;
using Rge.Finance.Api.Payables;
using Rge.Finance.Api.Policies;
using Rge.Finance.Api.Procurement;
using Rge.Finance.Api.Reporting;
using Rge.Finance.Api.Vendors;

namespace Rge.Finance.Api;

/// <summary>
/// Root endpoint module orchestrating all Finance Operations API endpoints (FIN-001 through FIN-058).
/// </summary>
public sealed class FinanceEndpoints : IEndpointModule
{
    private readonly CashAdvanceEndpoints _cashAdvances = new();
    private readonly DisbursementEndpoints _disbursements = new();
    private readonly ExpenseReportEndpoints _expenseReports = new();
    private readonly PayableEndpoints _payables = new();
    private readonly ProcurementEndpoints _procurement = new();
    private readonly PolicyEndpoints _policies = new();
    private readonly ExceptionEndpoints _exceptions = new();
    private readonly VendorEndpoints _vendors = new();
    private readonly ReportingEndpoints _reporting = new();

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _cashAdvances.MapEndpoints(app);
        _disbursements.MapEndpoints(app);
        _expenseReports.MapEndpoints(app);
        _payables.MapEndpoints(app);
        _procurement.MapEndpoints(app);
        _policies.MapEndpoints(app);
        _exceptions.MapEndpoints(app);
        _vendors.MapEndpoints(app);
        _reporting.MapEndpoints(app);
    }
}
