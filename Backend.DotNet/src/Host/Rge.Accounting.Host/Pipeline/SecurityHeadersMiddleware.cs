using Microsoft.AspNetCore.Http;

namespace Rge.Accounting.Host.Pipeline;

/// <summary>
/// Enforces OWASP ASVS security response headers (HSTS, CSP, X-Content-Type-Options,
/// X-Frame-Options, Referrer-Policy, and Permissions-Policy).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload";

            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        return _next(context);
    }
}

