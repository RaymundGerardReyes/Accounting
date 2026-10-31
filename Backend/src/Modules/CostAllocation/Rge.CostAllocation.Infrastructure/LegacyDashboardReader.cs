using Rge.CostAllocation.Application.Ports;

namespace Rge.CostAllocation.Infrastructure;

public sealed class LegacyDashboardReader
{
    private readonly ICostAllocationStore _store;

    public LegacyDashboardReader(ICostAllocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public object BuildDashboardSummary()
    {
        CostAllocationSnapshotDto snapshot = _store.GetSnapshot();

        long totalCostAll = Math.Max(1, snapshot.Departments.Sum(static d => d.TotalCostMinor));
        var departmentData = snapshot.Departments.Select((d, idx) => new
        {
            name = d.Name,
            value = Math.Round(((double)d.TotalCostMinor / totalCostAll) * 100.0, 1),
            color = $"hsl(var(--color-chart-{(idx % 5) + 1}))"
        }).ToList();

        var departmentMetrics = snapshot.Departments.Select(static d =>
        {
            double allocated = d.BudgetLimitMinor / 100.0;
            double actual = d.TotalCostMinor / 100.0;
            double variance = (d.BudgetLimitMinor - d.TotalCostMinor) / 100.0;
            string status = d.VarianceMinor < 0 ? "unfavorable" : d.VarianceMinor > 0 ? "favorable" : "neutral";
            double variancePercent = d.BudgetLimitMinor > 0 ? Math.Round(((double)d.VarianceMinor / d.BudgetLimitMinor) * 100.0, 1) : 0;
            return new
            {
                name = d.Name,
                allocated,
                actual,
                variance,
                status,
                variance_percent = variancePercent
            };
        }).ToList();

        var costTrendData = new[]
        {
            new { month = "Jan", allocated = 125000, actual = 128000, variance = -3000, variance_percent = -2.4 },
            new { month = "Feb", allocated = 132000, actual = 129500, variance = 2500, variance_percent = 1.9 },
            new { month = "Mar", allocated = 128000, actual = 135000, variance = -7000, variance_percent = -5.5 },
            new { month = "Apr", allocated = 140000, actual = 138000, variance = 2000, variance_percent = 1.4 },
            new { month = "May", allocated = 145000, actual = 142000, variance = 3000, variance_percent = 2.1 },
            new { month = "Jun", allocated = 152000, actual = 155000, variance = -3000, variance_percent = -2.0 }
        };

        var varianceTrendData = new[]
        {
            new { month = "Jan", favorable = 2, unfavorable = 2, neutral = 0 },
            new { month = "Feb", favorable = 3, unfavorable = 1, neutral = 0 },
            new { month = "Mar", favorable = 2, unfavorable = 2, neutral = 0 },
            new { month = "Apr", favorable = 3, unfavorable = 1, neutral = 0 },
            new { month = "May", favorable = 3, unfavorable = 1, neutral = 0 },
            new { month = "Jun", favorable = 2, unfavorable = 2, neutral = 0 }
        };

        return new
        {
            status = "ok",
            currency = snapshot.Currency,
            total_direct_cost_minor = snapshot.TotalDirectCostMinor,
            total_overhead_pool_minor = snapshot.TotalOverheadPoolMinor,
            departments = snapshot.Departments,
            products = snapshot.Products,
            department_metrics = departmentMetrics,
            department_data = departmentData,
            cost_trend_data = costTrendData,
            variance_trend_data = varianceTrendData
        };
    }

    public object BuildVarianceAnalysis()
    {
        CostAllocationSnapshotDto snapshot = _store.GetSnapshot();

        var varianceData = new[]
        {
            new { category = "Labor", budget = 180000.0, actual = 185000.0, variance = -5000.0, variance_percent = -2.8 },
            new { category = "Materials", budget = 150000.0, actual = 148000.0, variance = 2000.0, variance_percent = 1.3 },
            new { category = "Overhead", budget = 90000.0, actual = 92000.0, variance = -2000.0, variance_percent = -2.2 },
            new { category = "Utilities", budget = 30000.0, actual = 28000.0, variance = 2000.0, variance_percent = 6.7 }
        };

        var monthlyVariance = new[]
        {
            new { month = "Jan", variance = -3000.0, percentage = -2.4, favorable = 0, unfavorable = 1 },
            new { month = "Feb", variance = 2500.0, percentage = 1.9, favorable = 1, unfavorable = 0 },
            new { month = "Mar", variance = -7000.0, percentage = -5.5, favorable = 0, unfavorable = 1 },
            new { month = "Apr", variance = 2000.0, percentage = 1.4, favorable = 1, unfavorable = 0 },
            new { month = "May", variance = 3000.0, percentage = 2.1, favorable = 1, unfavorable = 0 },
            new { month = "Jun", variance = -3000.0, percentage = -2.0, favorable = 0, unfavorable = 1 }
        };

        var varianceReasons = new[]
        {
            new { reason = "Labor Rate Variance", amount = -4500.0, type = "unfavorable", impact = "High" },
            new { reason = "Material Price Variance", amount = 1800.0, type = "favorable", impact = "Medium" },
            new { reason = "Overhead Allocation", amount = -2000.0, type = "unfavorable", impact = "Medium" },
            new { reason = "Efficiency Variance", amount = 3200.0, type = "favorable", impact = "High" },
            new { reason = "Volume Variance", amount = -1200.0, type = "unfavorable", impact = "Low" }
        };

        var departmentVariance = snapshot.Departments.Count > 0
            ? snapshot.Departments.Select(static d => new
            {
                department = d.Name,
                budget = d.BudgetLimitMinor / 100.0,
                actual = d.TotalCostMinor / 100.0,
                variance = (d.BudgetLimitMinor - d.TotalCostMinor) / 100.0,
                status = d.VarianceMinor < 0 ? "unfavorable" : d.VarianceMinor > 0 ? "favorable" : "neutral"
            }).ToArray()
            :
            [
                new { department = "Manufacturing", budget = 450000.0, actual = 465000.0, variance = -15000.0, status = "unfavorable" },
                new { department = "Assembly", budget = 300000.0, actual = 292000.0, variance = 8000.0, status = "favorable" },
                new { department = "QA", budget = 150000.0, actual = 148000.0, variance = 2000.0, status = "favorable" },
                new { department = "Logistics", budget = 100000.0, actual = 100000.0, variance = 0.0, status = "neutral" }
            ];

        var variances = snapshot.Departments.Select(static d => new
        {
            department = d.Name,
            budget_limit_minor = d.BudgetLimitMinor,
            actual_total_minor = d.TotalCostMinor,
            variance_minor = d.VarianceMinor,
            is_favorable = d.VarianceMinor >= 0
        }).ToList();

        return new
        {
            currency = snapshot.Currency,
            variance_data = varianceData,
            monthly_variance = monthlyVariance,
            variance_reasons = varianceReasons,
            department_variance = departmentVariance,
            variances = variances
        };
    }

    public object? BuildDepartmentDetail(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CostDepartmentDto? dept = _store.GetDepartmentByName(name);
        string slug = name.Trim().ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
        if (slug.StartsWith("dept_", StringComparison.OrdinalIgnoreCase))
        {
            slug = slug switch
            {
                "dept_mfg" => "manufacturing",
                "dept_asm" => "assembly",
                "dept_qa" => "qa",
                "dept_log" => "logistics",
                _ => slug[5..]
            };
        }

        object[] costBreakdown;
        object[] monthlyTrend;
        object departmentStats;

        switch (slug)
        {
            case "manufacturing":
                costBreakdown =
                [
                    new { category = "Labor", allocated = 180000, actual = 185000, variance = -5000, variance_percent = -2.8 },
                    new { category = "Materials", allocated = 150000, actual = 148000, variance = 2000, variance_percent = 1.3 },
                    new { category = "Overhead", allocated = 90000, actual = 92000, variance = -2000, variance_percent = -2.2 },
                    new { category = "Utilities", allocated = 30000, actual = 28000, variance = 2000, variance_percent = 6.7 }
                ];
                monthlyTrend =
                [
                    new { month = "Jan", cost = 125000, budget = 120000, variance = -5000 },
                    new { month = "Feb", cost = 132000, budget = 125000, variance = -7000 },
                    new { month = "Mar", cost = 128000, budget = 125000, variance = -3000 },
                    new { month = "Apr", cost = 140000, budget = 135000, variance = -5000 },
                    new { month = "May", cost = 145000, budget = 140000, variance = -5000 },
                    new { month = "Jun", cost = 152000, budget = 150000, variance = -2000 }
                ];
                departmentStats = new
                {
                    allocated = 450000,
                    actual = 453000,
                    variance = -3000,
                    variance_percent = -0.67,
                    status = "unfavorable"
                };
                break;

            case "assembly":
                costBreakdown =
                [
                    new { category = "Assembly Labor", allocated = 140000, actual = 135000, variance = 5000, variance_percent = 3.6 },
                    new { category = "Components & Fasteners", allocated = 90000, actual = 88000, variance = 2000, variance_percent = 2.2 },
                    new { category = "Tooling Maintenance", allocated = 45000, actual = 44500, variance = 500, variance_percent = 1.1 },
                    new { category = "Assembly Line Power", allocated = 25000, actual = 24500, variance = 500, variance_percent = 2.0 }
                ];
                monthlyTrend =
                [
                    new { month = "Jan", cost = 48000, budget = 50000, variance = 2000 },
                    new { month = "Feb", cost = 49000, budget = 50000, variance = 1000 },
                    new { month = "Mar", cost = 47500, budget = 50000, variance = 2500 },
                    new { month = "Apr", cost = 50000, budget = 50000, variance = 0 },
                    new { month = "May", cost = 48500, budget = 50000, variance = 1500 },
                    new { month = "Jun", cost = 49000, budget = 50000, variance = 1000 }
                ];
                departmentStats = new
                {
                    allocated = 300000,
                    actual = 292000,
                    variance = 8000,
                    variance_percent = 2.67,
                    status = "favorable"
                };
                break;

            case "qa":
                costBreakdown =
                [
                    new { category = "Quality Inspection Labor", allocated = 75000, actual = 73500, variance = 1500, variance_percent = 2.0 },
                    new { category = "Testing Equipment & Reagents", allocated = 40000, actual = 39800, variance = 200, variance_percent = 0.5 },
                    new { category = "Calibration & Standards", allocated = 20000, actual = 19700, variance = 300, variance_percent = 1.5 },
                    new { category = "Compliance & Certifications", allocated = 15000, actual = 15000, variance = 0, variance_percent = 0.0 }
                ];
                monthlyTrend =
                [
                    new { month = "Jan", cost = 24500, budget = 25000, variance = 500 },
                    new { month = "Feb", cost = 25000, budget = 25000, variance = 0 },
                    new { month = "Mar", cost = 24000, budget = 25000, variance = 1000 },
                    new { month = "Apr", cost = 24800, budget = 25000, variance = 200 },
                    new { month = "May", cost = 24700, budget = 25000, variance = 300 },
                    new { month = "Jun", cost = 25000, budget = 25000, variance = 0 }
                ];
                departmentStats = new
                {
                    allocated = 150000,
                    actual = 148000,
                    variance = 2000,
                    variance_percent = 1.33,
                    status = "favorable"
                };
                break;

            case "logistics":
                costBreakdown =
                [
                    new { category = "Freight & Shipping", allocated = 50000, actual = 51000, variance = -1000, variance_percent = -2.0 },
                    new { category = "Warehousing & Storage", allocated = 25000, actual = 24500, variance = 500, variance_percent = 2.0 },
                    new { category = "Fleet Fuel & Maintenance", allocated = 15000, actual = 14800, variance = 200, variance_percent = 1.3 },
                    new { category = "Packaging Supplies", allocated = 10000, actual = 9700, variance = 300, variance_percent = 3.0 }
                ];
                monthlyTrend =
                [
                    new { month = "Jan", cost = 16500, budget = 16667, variance = 167 },
                    new { month = "Feb", cost = 16800, budget = 16667, variance = -133 },
                    new { month = "Mar", cost = 16500, budget = 16667, variance = 167 },
                    new { month = "Apr", cost = 16700, budget = 16667, variance = -33 },
                    new { month = "May", cost = 16600, budget = 16667, variance = 67 },
                    new { month = "Jun", cost = 16900, budget = 16667, variance = -233 }
                ];
                departmentStats = new
                {
                    allocated = 100000,
                    actual = 100000,
                    variance = 0,
                    variance_percent = 0.0,
                    status = "neutral"
                };
                break;

            default:
                if (dept is null)
                {
                    return null;
                }
                double dAllocated = dept.BudgetLimitMinor / 100.0;
                double dActual = dept.TotalCostMinor / 100.0;
                double dVariance = (dept.BudgetLimitMinor - dept.TotalCostMinor) / 100.0;
                string dStatus = dept.VarianceMinor < 0 ? "unfavorable" : dept.VarianceMinor > 0 ? "favorable" : "neutral";
                double dVarPct = dept.BudgetLimitMinor > 0 ? Math.Round(((double)dept.VarianceMinor / dept.BudgetLimitMinor) * 100.0, 2) : 0;
                costBreakdown =
                [
                    new { category = "Direct Operations", allocated = dAllocated * 0.65, actual = dept.DirectCostMinor / 100.0, variance = (dAllocated * 0.65) - (dept.DirectCostMinor / 100.0), variance_percent = 0.0 },
                    new { category = "Overhead Allocation", allocated = dAllocated * 0.35, actual = dept.AllocatedOverheadMinor / 100.0, variance = (dAllocated * 0.35) - (dept.AllocatedOverheadMinor / 100.0), variance_percent = 0.0 }
                ];
                monthlyTrend =
                [
                    new { month = "Jan", cost = dActual / 6.0, budget = dAllocated / 6.0, variance = (dAllocated - dActual) / 6.0 },
                    new { month = "Feb", cost = dActual / 6.0, budget = dAllocated / 6.0, variance = (dAllocated - dActual) / 6.0 },
                    new { month = "Mar", cost = dActual / 6.0, budget = dAllocated / 6.0, variance = (dAllocated - dActual) / 6.0 },
                    new { month = "Apr", cost = dActual / 6.0, budget = dAllocated / 6.0, variance = (dAllocated - dActual) / 6.0 },
                    new { month = "May", cost = dActual / 6.0, budget = dAllocated / 6.0, variance = (dAllocated - dActual) / 6.0 },
                    new { month = "Jun", cost = dActual / 6.0, budget = dAllocated / 6.0, variance = (dAllocated - dActual) / 6.0 }
                ];
                departmentStats = new
                {
                    allocated = dAllocated,
                    actual = dActual,
                    variance = dVariance,
                    variance_percent = dVarPct,
                    status = dStatus
                };
                break;
        }

        return new
        {
            cost_breakdown = costBreakdown,
            monthly_trend = monthlyTrend,
            department_stats = departmentStats,
            id = dept?.Id ?? $"dept_{slug}",
            name = dept?.Name ?? slug,
            cost_center_code = dept?.CostCenterCode ?? "CC-GEN",
            direct_cost_minor = dept?.DirectCostMinor ?? 0,
            overhead_share_minor = dept?.AllocatedOverheadMinor ?? 0,
            total_cost_minor = dept?.TotalCostMinor ?? 0,
            budget_limit_minor = dept?.BudgetLimitMinor ?? 0,
            variance_minor = dept?.VarianceMinor ?? 0,
            machine_hours = dept?.MachineHours ?? 0,
            labor_hours = dept?.LaborHours ?? 0,
            output_volume = dept?.OutputVolume ?? 0
        };
    }
}

