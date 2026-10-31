namespace Rge.Accounting.Host.Configuration;



/// <summary>

/// Configures strict, credential-safe CORS policies for the R.G.E Accounting frontend.

/// Never allows wildcard (*) origins alongside credentials.

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
            "http://localhost:5173",
            "https://localhost:7080",
            "http://localhost:5080",
            "https://rge-accounting-3aa707d5f496.herokuapp.com"
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

        string[] envOriginKeys = ["FRONTEND_ORIGIN", "NEXT_PUBLIC_APP_DOMAIN", "APP_DOMAIN"];
        foreach (string key in envOriginKeys)
        {
            string? val = configuration[key];
            if (!string.IsNullOrWhiteSpace(val))
            {
                originsSet.Add(val.TrimEnd('/'));
            }
        }

        string[] allowedOrigins = [.. originsSet];

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                policy.WithOrigins(allowedOrigins)
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
