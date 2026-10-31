using System.Text.Json;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.CostAllocation.Application.Ports;
using Rge.CostAllocation.Domain;

namespace Rge.CostAllocation.Infrastructure;

public sealed class JsonConfigurationStore : ICostAllocationStore
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();
    private readonly string? _filePath;
    private readonly List<CostDepartment> _departments =
    [
        new("dept_mfg", "Manufacturing", "CC-001", Money.Php(45_000_00), Money.Php(60_000_00), 400, 800, 1200),
        new("dept_asm", "Assembly", "CC-002", Money.Php(30_000_00), Money.Php(45_000_00), 250, 600, 1000),
        new("dept_qa", "QA", "CC-003", Money.Php(15_000_00), Money.Php(25_000_00), 100, 300, 500),
        new("dept_log", "Logistics", "CC-004", Money.Php(20_000_00), Money.Php(30_000_00), 150, 400, 800)
    ];

    private readonly List<CostProductDto> _products =
    [
        new("prod_01", "CPA Reviewer Volume 1", 850_00, 150_00, 500),
        new("prod_02", "Cost Accounting Manual", 950_00, 180_00, 350)
    ];

    private readonly List<CostActivityDto> _activities =
    [
        new("act_01", "Machine Setup & Print Run", 18_000_00, "MACHINE_HRS", "Machine Hours", 900),
        new("act_02", "Quality & Binding Inspection", 12_000_00, "LABOR_HRS", "Labor Hours", 2100)
    ];

    private readonly Dictionary<string, object> _config = new(StringComparer.OrdinalIgnoreCase);

    public JsonConfigurationStore()
    {
        _filePath = LocateConfigurationFile();
        InitializeConfig();
    }

    private static string? LocateConfigurationFile()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "data", "configuration.json"),
            Path.Combine(AppContext.BaseDirectory, "Backend", "data", "configuration.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Backend", "data", "configuration.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "configuration.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Backend", "data", "configuration.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Backend", "data", "configuration.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Backend", "data", "configuration.json")
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore path resolution errors
            }
        }

        DirectoryInfo? current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            string checkBackend = Path.Combine(current.FullName, "Backend", "data", "configuration.json");
            if (File.Exists(checkBackend))
            {
                return checkBackend;
            }

            string checkDirect = Path.Combine(current.FullName, "data", "configuration.json");
            if (File.Exists(checkDirect))
            {
                return checkDirect;
            }

            current = current.Parent;
        }

        return null;
    }

    private void InitializeConfig()
    {
        if (_filePath is not null && File.Exists(_filePath))
        {
            try
            {
                string json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (loaded is not null)
                {
                    foreach (var kvp in loaded)
                    {
                        _config[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch
            {
                // Fall back to default seeded structure
            }
        }

        if (!_config.ContainsKey("config"))
        {
            _config["config"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["companyName"] = "R.G.E Accounting Firm Inc.",
                ["fiscalYear"] = "2025",
                ["allocationMethod"] = "activity-based",
                ["reportingCurrency"] = "PHP",
                ["decimalPlaces"] = 2,
                ["autoBackup"] = true,
                ["notificationEmail"] = "admin@acme.com"
            };
        }

        if (!_config.ContainsKey("departments"))
        {
            _config["departments"] = new List<object>
            {
                new Dictionary<string, object> { ["id"] = "manufacturing", ["name"] = "Manufacturing", ["costCenter"] = "CC-001", ["manager"] = "John Smith", ["budget"] = 450000 },
                new Dictionary<string, object> { ["id"] = "assembly", ["name"] = "Assembly", ["costCenter"] = "CC-002", ["manager"] = "Sarah Johnson", ["budget"] = 300000 },
                new Dictionary<string, object> { ["id"] = "qa", ["name"] = "QA", ["costCenter"] = "CC-003", ["manager"] = "Mike Davis", ["budget"] = 150000 },
                new Dictionary<string, object> { ["id"] = "logistics", ["name"] = "Logistics", ["costCenter"] = "CC-004", ["manager"] = "Lisa Chen", ["budget"] = 100000 }
            };
        }

        if (!_config.ContainsKey("users"))
        {
            _config["users"] = new List<object>
            {
                new Dictionary<string, object> { ["id"] = 1, ["name"] = "John Smith", ["role"] = "System Administrator", ["email"] = "admin@rge.com.ph", ["status"] = "Active" },
                new Dictionary<string, object> { ["id"] = 2, ["name"] = "Sarah Johnson", ["role"] = "Cost Analyst", ["email"] = "sarah@acme.com", ["status"] = "Active" },
                new Dictionary<string, object> { ["id"] = 3, ["name"] = "Mike Davis", ["role"] = "Auditor", ["email"] = "mike@acme.com", ["status"] = "Active" },
                new Dictionary<string, object> { ["id"] = 4, ["name"] = "Lisa Chen", ["role"] = "Department Manager", ["email"] = "lisa@acme.com", ["status"] = "Inactive" }
            };
        }

        if (!_config.ContainsKey("auditLogs"))
        {
            _config["auditLogs"] = new List<object>
            {
                new Dictionary<string, object> { ["id"] = 774, ["action"] = "Payment received: \u20b189.00 for ORD-202610-2581 (QRPh Paid Test Order)", ["user"] = "PayMongo Gateway", ["time"] = "Just now", ["severity"] = "success" },
                new Dictionary<string, object> { ["id"] = 773, ["action"] = "System configuration updated: Default Allocation Basis set to activity-based", ["user"] = "System Administrator", ["time"] = "1 hour ago", ["severity"] = "info" }
            };
        }

        if (!_config.ContainsKey("integrations"))
        {
            _config["integrations"] = new List<object>
            {
                new Dictionary<string, object> { ["name"] = "PayMongo Payment Gateway", ["status"] = "Connected & Operational", ["lastSync"] = "Just now", ["type"] = "payment" },
                new Dictionary<string, object> { ["name"] = "Database", ["status"] = "Connected & Operational", ["lastSync"] = "Real-time", ["type"] = "database" },
                new Dictionary<string, object> { ["name"] = "Email Notifications", ["status"] = "Connected & Operational", ["lastSync"] = "Real-time", ["type"] = "notification" },
                new Dictionary<string, object> { ["name"] = "API Access", ["status"] = "Connected & Operational", ["lastSync"] = "Real-time", ["type"] = "api" }
            };
        }

        // Backward compatibility flat keys
        _config["company_name"] = "R.G.E Accounting Firm Inc.";
        _config["currency"] = "PHP";
        _config["vat_rate_bps"] = 1200;
    }

    public CostAllocationSnapshotDto GetSnapshot()
    {
        lock (_lock)
        {
            long totalMachineHours = Math.Max(1, _departments.Sum(d => d.MachineHours));
            Money totalOverheadPool = Money.Php(_activities.Sum(a => a.OverheadPoolMinor));

            List<CostDepartmentDto> deptDtos = [];
            foreach (CostDepartment dept in _departments)
            {
                Money overheadShare = AllocationCalculator.AllocatePoolShare(
                    totalOverheadPool,
                    dept.MachineHours,
                    totalMachineHours);

                Money totalCost = dept.DirectCost + overheadShare;
                Money variance = dept.BudgetLimit - totalCost;

                deptDtos.Add(new CostDepartmentDto(
                    dept.Id,
                    dept.Name,
                    dept.CostCenterCode,
                    dept.DirectCost.AmountMinor,
                    overheadShare.AmountMinor,
                    totalCost.AmountMinor,
                    dept.BudgetLimit.AmountMinor,
                    variance.AmountMinor,
                    dept.MachineHours,
                    dept.LaborHours,
                    dept.OutputVolume));
            }

            return new CostAllocationSnapshotDto(
                Departments: deptDtos,
                Products: _products,
                Activities: _activities,
                TotalDirectCostMinor: deptDtos.Sum(d => d.DirectCostMinor),
                TotalOverheadPoolMinor: totalOverheadPool.AmountMinor,
                Currency: "PHP");
        }
    }

    public CostDepartmentDto? GetDepartmentByName(string name) =>
        GetSnapshot().Departments.FirstOrDefault(
            d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(d.Id, name, StringComparison.OrdinalIgnoreCase));

    public CostDepartmentDto? UpdateDepartmentMetrics(
        string departmentName,
        long directCostMinor,
        long machineHours,
        long laborHours,
        long outputVolume)
    {
        lock (_lock)
        {
            CostDepartment? dept = _departments.FirstOrDefault(
                d => string.Equals(d.Name, departmentName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(d.Id, departmentName, StringComparison.OrdinalIgnoreCase));

            if (dept is null)
            {
                return null;
            }

            dept.UpdateMetrics(Money.Php(directCostMinor), machineHours, laborHours, outputVolume);
        }

        return GetDepartmentByName(departmentName);
    }

    public Dictionary<string, object> GetConfiguration()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>(_config, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveConfiguration(Dictionary<string, object> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_lock)
        {
            foreach (var kvp in config)
            {
                _config[kvp.Key] = kvp.Value;
            }

            if (_filePath is not null)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_config, IndentedJsonOptions);
                    File.WriteAllText(_filePath, json);
                }
                catch
                {
                    // In-memory update succeeded; disk persistence failure is non-fatal
                }
            }
        }
    }
}
