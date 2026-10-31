using Microsoft.Extensions.DependencyInjection;
using Rge.CostAllocation.Application.GetCostAllocation;
using Rge.CostAllocation.Application.Ports;
using Rge.CostAllocation.Application.UpdateDepartmentCost;

namespace Rge.CostAllocation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCostAllocationModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICostAllocationStore, JsonConfigurationStore>();
        services.AddSingleton<LegacyDashboardReader>();
        services.AddScoped<GetCostAllocationHandler>();
        services.AddScoped<UpdateDepartmentCostHandler>();

        return services;
    }
}

