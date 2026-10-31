using System.Reflection;
using Rge.Accounting.Host.Composition;
using Rge.Accounting.Host.Configuration;
using Rge.Accounting.Host.Pipeline;
using Rge.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 1. Harden Kestrel transport & payload limits
builder.ConfigureHardenedKestrel();
// 2. Fail fast if secrets are missing or using compromised legacy keys in non-Development
SecretsConfiguration.ValidateRequiredSecretsOrThrow(builder.Configuration, builder.Environment);
// 3. Register platform services, JSON snake_case source-generation, rate limiting, and observability
builder.Services.AddAccountingPlatformHost(builder.Configuration);
var app = builder.Build();

// Seed default development credentials if running in Development or FINANCE_SEED == "dev"
await IdentityDbSeeder.SeedDevelopmentCredentialsAsync(app.Services);
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

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseExceptionHandler();

app.UseMiddleware<RequestSizeLimitPolicy>();

app.UseCors(CorsConfiguration.PolicyName);

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.UseMiddleware<IdempotencyKeyMiddleware>();

app.UseMiddleware<RequestLoggingEnricher>();



// 6. Map probes (/health, /api/health, /version) and all registered modules
app.MapAccountingPlatformEndpoints(cachedVersionPayload);

await app.RunAsync();

public partial class Program;

