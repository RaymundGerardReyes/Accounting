using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Accounting.Host.Configuration;

/// <summary>
/// Configures per-IP and per-actor rate limiting buckets across critical API edge routes.
/// </summary>
public static class RateLimiterConfiguration
{
    public const string LoginBucket = "RateLimit.Login";
    public const string WebhookBucket = "RateLimit.Webhook";
    public const string UploadBucket = "RateLimit.Upload";
    public const string GeneralApiBucket = "RateLimit.GeneralApi";

    public static IServiceCollection AddEdgeRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                var error = new Error(
                    "Security.RateLimitExceeded",
                    "Too many requests. Please wait before retrying.",
                    ErrorType.Validation);

                var problem = ProblemDetailsFactory.Create(context.HttpContext, error);
                problem.Status = StatusCodes.Status429TooManyRequests;
                problem.Title = "Too Many Requests";

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };

            // 1. Strict Login Bucket: 5 attempts per minute per IP (matches 5-attempt DB lockout)
            options.AddPolicy(LoginBucket, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 2. Webhook Intake Bucket: 120 bursts per minute per IP
            options.AddPolicy(WebhookBucket, httpContext =>
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: GetClientIp(httpContext),
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 120,
                        TokensPerPeriod = 120,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 3. Document Upload Bucket: 15 uploads per minute per authenticated user or IP
            options.AddPolicy(UploadBucket, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetActorOrIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 15,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // 4. General API Bucket: 300 requests per minute per actor or IP
            options.AddPolicy(GeneralApiBucket, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: GetActorOrIp(httpContext),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private static string GetClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";

    private static string GetActorOrIp(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(context.User.Identity.Name)
            ? context.User.Identity.Name
            : GetClientIp(context);
}

