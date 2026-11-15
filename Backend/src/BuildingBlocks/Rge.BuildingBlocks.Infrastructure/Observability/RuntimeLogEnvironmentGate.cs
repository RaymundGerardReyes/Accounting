using System;
using System.IO;

namespace Rge.BuildingBlocks.Infrastructure.Observability;

public enum RuntimeLoggingMode
{
    ProductionLocked = 0,
    DevelopmentFullBlast = 1
}

public static class RuntimeLogEnvironmentGate
{
    private static int _initialized;
    private static string _environmentName = "Production";
    private static RuntimeLoggingMode _mode = RuntimeLoggingMode.ProductionLocked;

    public static string EnvironmentName
    {
        get
        {
            EnsureInitialized();
            return _environmentName;
        }
    }

    public static RuntimeLoggingMode Mode
    {
        get
        {
            EnsureInitialized();
            return _mode;
        }
    }

    /// <summary>
    /// True IF AND ONLY IF ASPNETCORE_ENVIRONMENT == "Development".
    /// </summary>
    public static bool IsDevelopmentFullBlast => Mode == RuntimeLoggingMode.DevelopmentFullBlast;

    /// <summary>
    /// True whenever ASPNETCORE_ENVIRONMENT == "Production" (or any non-Development environment).
    /// </summary>
    public static bool IsProductionLocked => Mode == RuntimeLoggingMode.ProductionLocked;

    public static void InitializeFromDotEnvAndEnvironment(string? contentRootPath = null)
    {
        LoadDotEnvIfPresent(contentRootPath);

        var rawEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";

        _environmentName = rawEnv.Trim();

        // STRICT INVARIANT:
        // - If "Production" -> BLOCKED (ProductionLocked)
        // - If and only if "Development" -> ENABLED (DevelopmentFullBlast)
        // - Any other value -> Fail-closed to ProductionLocked
        if (string.Equals(_environmentName, "Development", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            _mode = RuntimeLoggingMode.DevelopmentFullBlast;
        }
        else
        {
            _mode = RuntimeLoggingMode.ProductionLocked;
        }

        _initialized = 1;
    }

    public static void SetModeForTesting(RuntimeLoggingMode mode, string environmentName)
    {
        _mode = mode;
        _environmentName = environmentName;
        _initialized = 1;
    }

    private static void EnsureInitialized()
    {
        if (_initialized == 0)
        {
            InitializeFromDotEnvAndEnvironment();
        }
    }

    private static void LoadDotEnvIfPresent(string? startDirectory)
    {
        try
        {
            var dir = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
            while (dir is not null)
            {
                var envFile = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envFile))
                {
                    foreach (var rawLine in File.ReadAllLines(envFile))
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                        {
                            continue;
                        }

                        var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
                        if (separatorIndex <= 0)
                        {
                            continue;
                        }

                        var key = line[..separatorIndex].Trim();
                        var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');

                        // Only populate from .env if not already explicitly set in OS environment
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        {
                            Environment.SetEnvironmentVariable(key, value);
                        }
                    }
                    break;
                }
                dir = dir.Parent;
            }
        }
        catch
        {
            // Fail-safe: if .env cannot be read, rely on OS environment variables (defaulting to ProductionLocked)
        }
    }
}
