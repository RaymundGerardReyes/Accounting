using System.Collections.Concurrent;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Infrastructure.Policy;

/// <summary>
/// Thread-safe in-memory cache for evaluated financial policies and policy versions.
/// </summary>
public sealed class PolicyCache
{
    private readonly ConcurrentDictionary<string, FinancialPolicy> _policiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FinancialPolicy> _policiesByCode = new(StringComparer.OrdinalIgnoreCase);

    public FinancialPolicy? GetById(string policyId)
    {
        if (string.IsNullOrWhiteSpace(policyId))
        {
            return null;
        }

        return _policiesById.TryGetValue(policyId.Trim(), out FinancialPolicy? policy) ? policy : null;
    }

    public FinancialPolicy? GetByCode(string policyCode)
    {
        if (string.IsNullOrWhiteSpace(policyCode))
        {
            return null;
        }

        return _policiesByCode.TryGetValue(policyCode.Trim(), out FinancialPolicy? policy) ? policy : null;
    }

    public void Set(FinancialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _policiesById[policy.Id] = policy;
        _policiesByCode[policy.PolicyCode] = policy;
    }

    public void Remove(string policyId)
    {
        if (string.IsNullOrWhiteSpace(policyId))
        {
            return;
        }

        if (_policiesById.TryRemove(policyId.Trim(), out FinancialPolicy? removed))
        {
            _policiesByCode.TryRemove(removed.PolicyCode, out _);
        }
    }

    public void Clear()
    {
        _policiesById.Clear();
        _policiesByCode.Clear();
    }
}
