using Microsoft.Extensions.DependencyInjection;
using Rge.Audit.Application.AppendAuditEvent;
using Rge.Audit.Application.Ports;
using Rge.Audit.Application.QueryAuditEvents;
using Rge.Audit.Application.VerifyChain;
using Rge.Audit.Contracts;

namespace Rge.Audit.Infrastructure;

/// <summary>
/// Registers cryptographic Audit Module services, repositories, and application query/command handlers.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<PgAuditAppender>();
        services.AddScoped<IAuditTrail>(sp => sp.GetRequiredService<PgAuditAppender>());
        services.AddScoped<Rge.BuildingBlocks.Application.Behaviors.IAuditBehaviorWriter>(sp => sp.GetRequiredService<PgAuditAppender>());

        services.AddScoped<AuditQueryReader>();
        services.AddScoped<IAuditReader>(sp => sp.GetRequiredService<AuditQueryReader>());

        services.AddScoped<ChainVerifier>();
        services.AddScoped<IChainVerifier>(sp => sp.GetRequiredService<ChainVerifier>());

        services.AddScoped<AppendAuditEventHandler>();
        services.AddScoped<QueryAuditEventsHandler>();
        services.AddScoped<VerifyChainHandler>();

        return services;
    }
}

