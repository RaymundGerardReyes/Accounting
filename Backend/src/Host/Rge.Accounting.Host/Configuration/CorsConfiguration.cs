namespace Rge.Accounting.Host.Configuration;

/// <summary>
/// Configures strict, credential-safe CORS policies for the R.G.E Accounting frontend.
/// Dynamically resolves origins while strictly echoing the requesting origin to satisfy
/// W3C Access-Control-Allow-Credentials requirements (never uses '*').
/// </summary>
public static class CorsConfiguration
{
    public const string PolicyName = "RgeStrictCorsPolicy";

    public static IServiceCollection AddStrictCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var originsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "http://localhost:3000",
            "http://localhost:3001",
            "http://localhost:5173",
            "http://localhost:4173",
            "https://localhost:7080",
            "http://localhost:5080",
            "https://rge-accounting-3aa707d5f496.herokuapp.com",
            "https://accounting.raymundgerardestaca.dev",
            "https://raymundgerardestaca.dev",
            "https://www.raymundgerardestaca.dev"
        };

        string[]? configuredOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        if (configuredOrigins is not null)
        {
            foreach (string origin in configuredOrigins)
            {
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    originsSet.Add(origin.TrimEnd('/'));
                }
            }
        }

        string[] envOriginKeys = [
            "FRONTEND_ORIGIN",
            "FRONTEND_URL",
            "NEXT_PUBLIC_APP_DOMAIN",
            "APP_DOMAIN",
            "NEXT_PUBLIC_APP_URL",
            "APP_URL",
            "ALLOWED_ORIGINS",
            "CORS_ALLOWED_ORIGINS"
        ];

        foreach (string key in envOriginKeys)
        {
            string? val = configuration[key] ?? Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(val))
            {
                foreach (string part in val.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    originsSet.Add(part.TrimEnd('/'));
                }
            }
        }

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                policy.SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrWhiteSpace(origin))
                    {
                        return false;
                    }

                    string normalized = origin.TrimEnd('/');
                    if (originsSet.Contains(normalized))
                    {
                        return true;
                    }

                    if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        string host = uri.Host;
                        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (host.EndsWith(".raymundgerardestaca.dev", StringComparison.OrdinalIgnoreCase) ||
                            host.Equals("raymundgerardestaca.dev", StringComparison.OrdinalIgnoreCase) ||
                            host.EndsWith(".herokuapp.com", StringComparison.OrdinalIgnoreCase) ||
                            host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                })
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .WithHeaders(
                    "Content-Type",
                    "Authorization",
                    "X-CSRF-Token",
                    "X-Correlation-Id",
                    "X-Correlation-ID",
                    "Idempotency-Key",
                    "X-Idempotency-Key",
                    "X-User-Id",
                    "X-User-ID",
                    "X-Act-As-User",
                    "X-Act-As-User-ID",
                    "X-Google-Forms-Secret",
                    "Paymongo-Signature",
                    "Accept",
                    "Origin",
                    "X-Requested-With")
                .WithExposedHeaders("X-Correlation-Id", "X-Correlation-ID")
                .AllowCredentials();
            });
        });

        return services;
    }
}
