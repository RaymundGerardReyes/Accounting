using Rge.BuildingBlocks.Api.Endpoints;



namespace Rge.Accounting.Host.Composition;



/// <summary>

/// Maps all registered module endpoints and core health/version probes.

/// </summary>

public static class EndpointRouteBuilderExtensions

{

    public static WebApplication MapAccountingPlatformEndpoints(

        this WebApplication app,

        object cachedVersionPayload)

    {

        ArgumentNullException.ThrowIfNull(app);

        ArgumentNullException.ThrowIfNull(cachedVersionPayload);



        // Pre-computed /version and /health probes (zero reflection per request)

        app.MapGet("/version", () => Results.Ok(cachedVersionPayload))

           .WithTags("Probes");



        var healthResponse = new { status = "healthy", line = "dotnet" };

        app.MapGet("/health", () => Results.Ok(healthResponse))

           .WithTags("Probes");

        app.MapGet("/api/health", () => Results.Ok(healthResponse))

           .WithTags("Probes");



        ModuleRegistry registry = app.Services.GetRequiredService<ModuleRegistry>();

        foreach (IEndpointModule module in registry.Modules)

        {

            module.MapEndpoints(app);

        }



        return app;

    }

}
