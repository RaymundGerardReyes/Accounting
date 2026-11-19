using System.Reflection;
using Rge.Accounting.Host.Composition;
using Rge.Accounting.Host.Configuration;
using Rge.Accounting.Host.Pipeline;
using Rge.BuildingBlocks.Infrastructure.Observability;
using Rge.Identity.Infrastructure;

// 0. Initialize RuntimeLogEnvironmentGate from .env and environment
RuntimeLogEnvironmentGate.InitializeFromDotEnvAndEnvironment(Directory.GetCurrentDirectory());

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DYNO")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
}

var builder = WebApplication.CreateBuilder(args);

// Load root .env file if present
var searchDir = new DirectoryInfo(builder.Environment.ContentRootPath);
while (searchDir != null)
{
    var envFile = Path.Combine(searchDir.FullName, ".env");
    if (File.Exists(envFile))
    {
        var lines = await File.ReadAllLinesAsync(envFile).ConfigureAwait(false);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#') || !trimmed.Contains('=', StringComparison.Ordinal))
            {
                continue;
            }
            var parts = trimmed.Split('=', 2);
            var key = parts[0].Trim();
            var val = parts[1].Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, val);
            }
            builder.Configuration[key] = val;
        }
        break;
    }
    searchDir = searchDir.Parent;
}

var configuredUrls = builder.Configuration["Urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrEmpty(configuredUrls))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5080;http://127.0.0.1:5000");
}

// 1. Harden Kestrel transport & payload limits
builder.ConfigureHardenedKestrel();
// 2. Fail fast if secrets are missing or using compromised legacy keys in non-Development
SecretsConfiguration.ValidateRequiredSecretsOrThrow(builder.Configuration, builder.Environment);

builder.Host.UseDefaultServiceProvider((context, options) =>
{
    bool validate = context.HostingEnvironment.IsDevelopment() || context.HostingEnvironment.IsEnvironment("Testing");
    options.ValidateScopes = validate;
    options.ValidateOnBuild = validate;
});

// 3. Register platform services, JSON snake_case source-generation, rate limiting, and observability
builder.Services.AddAccountingPlatformHost(builder.Configuration);
var app = builder.Build();

// Seed default development credentials if running in Development or FINANCE_SEED == "dev"
await IdentityDbSeeder.SeedDevelopmentCredentialsAsync(app.Services);

// Warm the database connection pool at startup to eliminate cold TLS handshakes on hot paths
try
{
    var dataSource = app.Services.GetService<System.Data.Common.DbDataSource>();
    if (dataSource != null)
    {
        await using var warmupConn = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
    }
}
catch (Exception)
{
    // Non-fatal if running in offline test harness or disconnected container
}
// 4. Pre-compute version payload once at startup (zero reflection on hot requests)
var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;

string infoVersion = entryAssembly

    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion

    ?? "0.1.0-preview.0";

string[] versionParts = infoVersion.Split('+');

var cachedVersionPayload = new

{

    line = "dotnet",

    version = versionParts[0],

    git_sha = versionParts.Length > 1 ? versionParts[1] : "local-dev",

    build_time = DateTimeOffset.UtcNow.ToString("O"),

    api_contract = "v1",

    schema_version = "V0004"

};



// 5. Ordered Zero-Trust Request Pipeline

app.UseForwardedHeaders();

app.UseMiddleware<PathNormalizationMiddleware>();

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseExceptionHandler();

app.UseMiddleware<RequestSizeLimitPolicy>();

app.UseRouting();

app.UseCors(CorsConfiguration.PolicyName);

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(15)
});

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.UseMiddleware<IdempotencyKeyMiddleware>();

app.UseMiddleware<RequestLoggingEnricher>();



// 6. Map probes (/health, /api/health, /version) and all registered modules
app.MapAccountingPlatformEndpoints(cachedVersionPayload);

// 7. Map Centralized Runtime Logs Controller (enabled in Development, 403 Forbidden in Production)
app.MapCentralizedRuntimeLogsController();

await app.RunAsync();

public partial class Program;

