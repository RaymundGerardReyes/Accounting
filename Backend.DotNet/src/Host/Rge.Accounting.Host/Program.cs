using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/version", () =>
{
    var assembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
    var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0-preview.0";
    
    var parts = infoVersion.Split('+');
    var version = parts[0];
    var gitSha = parts.Length > 1 ? parts[1] : "local-dev";

    return Results.Ok(new
    {
        line = "dotnet",
        version = version,
        gitSha = gitSha,
        buildTime = DateTimeOffset.UtcNow.ToString("O"),
        apiContract = "v1",
        schemaVersion = "V0004"
    });
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", line = "dotnet" }));

app.Run();

