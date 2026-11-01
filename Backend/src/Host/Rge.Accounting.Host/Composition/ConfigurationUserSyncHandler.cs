using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rge.CostAllocation.Contracts;
using Rge.Identity.Application.Ports;
using Rge.Identity.Domain;

namespace Rge.Accounting.Host.Composition;

/// <summary>
/// Handles synchronization of users configured in Cost Allocation configuration
/// directly into the authoritative IRoleStore and IActorCache.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via DI")]
internal sealed class ConfigurationUserSyncHandler : IConfigurationUserSyncHandler
{
    private readonly IRoleStore _roleStore;
    private readonly IActorCache _actorCache;
    private readonly ILogger<ConfigurationUserSyncHandler> _logger;

    public ConfigurationUserSyncHandler(
        IRoleStore roleStore,
        IActorCache actorCache,
        ILogger<ConfigurationUserSyncHandler> logger)
    {
        _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
        _actorCache = actorCache ?? throw new ArgumentNullException(nameof(actorCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SyncConfigurationUsersAsync(object usersPayload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usersPayload);

        try
        {
            List<ConfiguredUserItem> items = ParseUsersPayload(usersPayload);
            if (items.Count == 0)
            {
                return;
            }

            foreach (ConfiguredUserItem item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Email))
                {
                    continue;
                }

                string userId = !string.IsNullOrWhiteSpace(item.Id)
                    ? (item.Id.StartsWith("usr_", StringComparison.OrdinalIgnoreCase) ? item.Id : $"usr_cfg_{item.Id}")
                    : $"usr_cfg_{Math.Abs(item.Email.GetHashCode(StringComparison.OrdinalIgnoreCase))}";

                string employeeCode = !string.IsNullOrWhiteSpace(item.EmployeeCode)
                    ? item.EmployeeCode
                    : $"EMP-CFG-{Math.Abs(userId.GetHashCode(StringComparison.OrdinalIgnoreCase)) % 10000:D4}";

                string fullName = !string.IsNullOrWhiteSpace(item.FullName)
                    ? item.FullName
                    : item.Email.Split('@')[0];

                string mappedRole = MapRoleToRbac(item.Role);
                bool isActive = string.IsNullOrWhiteSpace(item.Status) ||
                                string.Equals(item.Status, "active", StringComparison.OrdinalIgnoreCase);

                await _roleStore.UpsertUserCredentialsAndRolesAsync(
                    userId: userId,
                    employeeCode: employeeCode,
                    fullName: fullName,
                    email: item.Email.Trim().ToLowerInvariant(),
                    username: item.Email.Split('@')[0].ToLowerInvariant(),
                    password: null,
                    departmentId: "finance",
                    costCenterId: "CC-FIN",
                    employeeGroup: "management",
                    roles: [mappedRole],
                    departmentScope: "*",
                    isActive: isActive,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _actorCache.Invalidate(userId);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Synchronized configuration user {Email} with role {Role} into Identity store.", item.Email, mappedRole);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synchronize configuration users into Identity store.");
        }
    }

    private static List<ConfiguredUserItem> ParseUsersPayload(object payload)
    {
        var result = new List<ConfiguredUserItem>();

        if (payload is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in jsonElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        string id = GetStringProperty(element, "id");
                        string name = GetStringProperty(element, "name", "full_name", "fullName");
                        string email = GetStringProperty(element, "email");
                        string role = GetStringProperty(element, "role");
                        string status = GetStringProperty(element, "status");
                        string empCode = GetStringProperty(element, "employee_code", "employeeCode");

                        result.Add(new ConfiguredUserItem(id, empCode, name, email, role, status));
                    }
                }
            }
            return result;
        }

        // Handle case where payload is already deserialized as IEnumerable (Dictionary or object list)
        if (payload is System.Collections.IEnumerable enumerable)
        {
            foreach (object item in enumerable)
            {
                if (item is JsonElement el)
                {
                    string id = GetStringProperty(el, "id");
                    string name = GetStringProperty(el, "name", "full_name", "fullName");
                    string email = GetStringProperty(el, "email");
                    string role = GetStringProperty(el, "role");
                    string status = GetStringProperty(el, "status");
                    string empCode = GetStringProperty(el, "employee_code", "employeeCode");

                    result.Add(new ConfiguredUserItem(id, empCode, name, email, role, status));
                }
                else if (item is IDictionary<string, object> dict)
                {
                    string id = dict.TryGetValue("id", out var vId) ? vId?.ToString() ?? "" : "";
                    string name = dict.TryGetValue("name", out var vName) ? vName?.ToString() ?? "" :
                                 (dict.TryGetValue("full_name", out var vFn) ? vFn?.ToString() ?? "" : "");
                    string email = dict.TryGetValue("email", out var vMail) ? vMail?.ToString() ?? "" : "";
                    string role = dict.TryGetValue("role", out var vRole) ? vRole?.ToString() ?? "" : "";
                    string status = dict.TryGetValue("status", out var vSt) ? vSt?.ToString() ?? "" : "";
                    string empCode = dict.TryGetValue("employee_code", out var vCode) ? vCode?.ToString() ?? "" : "";

                    result.Add(new ConfiguredUserItem(id, empCode, name, email, role, status));
                }
            }
        }

        return result;
    }

    private static string GetStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (string propName in propertyNames)
        {
            if (element.TryGetProperty(propName, out JsonElement prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString() ?? string.Empty;
                }
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.ToString();
                }
            }
        }
        return string.Empty;
    }

    private static string MapRoleToRbac(string? roleCandidate)
    {
        if (string.IsNullOrWhiteSpace(roleCandidate))
        {
            return Role.Employee;
        }

        string normalized = roleCandidate.Trim();
        if (normalized.Contains("admin", StringComparison.OrdinalIgnoreCase)) return Role.SystemAdmin;
        if (normalized.Contains("controller", StringComparison.OrdinalIgnoreCase)) return Role.FinanceController;
        if (normalized.Contains("reviewer", StringComparison.OrdinalIgnoreCase)) return Role.FinanceReviewer;
        if (normalized.Contains("supervisor", StringComparison.OrdinalIgnoreCase)) return Role.Supervisor;
        if (normalized.Contains("maker", StringComparison.OrdinalIgnoreCase)) return Role.Maker;
        if (normalized.Contains("verifier", StringComparison.OrdinalIgnoreCase) && !normalized.Contains("evidence", StringComparison.OrdinalIgnoreCase)) return Role.Verifier;
        if (normalized.Contains("evidence", StringComparison.OrdinalIgnoreCase)) return Role.EvidenceVerifier;
        if (normalized.Contains("bank", StringComparison.OrdinalIgnoreCase)) return Role.BankApprover;
        if (normalized.Contains("auditor", StringComparison.OrdinalIgnoreCase)) return Role.Auditor;

        return Role.Employee;
    }

    private sealed record ConfiguredUserItem(
        string Id,
        string EmployeeCode,
        string FullName,
        string Email,
        string Role,
        string Status);
}

