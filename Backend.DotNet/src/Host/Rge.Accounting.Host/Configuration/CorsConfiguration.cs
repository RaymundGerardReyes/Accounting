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



        string[] allowedOrigins = configuration

            .GetSection("Cors:AllowedOrigins")

            .Get<string[]>() ?? ["http://localhost:5173", "http://localhost:3000"];



        services.AddCors(options =>

        {

            options.AddPolicy(PolicyName, policy =>

            {

                policy.WithOrigins(allowedOrigins)

                      .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")

                      .WithHeaders("Content-Type", "Authorization", "X-CSRF-Token", "X-Correlation-Id", "Idempotency-Key", "X-Idempotency-Key", "X-User-Id", "X-Act-As-User", "X-Google-Forms-Secret", "Paymongo-Signature")

                      .WithExposedHeaders("X-Correlation-Id")

                      .AllowCredentials();

            });

        });



        return services;

    }

}
