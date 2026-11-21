using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Rge.Identity.Infrastructure.OAuth;

public interface IGoogleRedirectUriBuilder
{
    /// <summary>
    /// Returns the single canonical Google OAuth redirect_uri so OAuthBegin
    /// and OAuthCallback (token exchange) always send the exact same value.
    /// </summary>
    string Build(HttpContext httpContext);
}

public sealed class GoogleRedirectUriBuilder : IGoogleRedirectUriBuilder
{
    private const string DefaultCallbackPath = "/api/auth/google/callback";
    private readonly IConfiguration _config;
    private readonly string? _defaultOrigin;

    public GoogleRedirectUriBuilder(IConfiguration config, string? defaultOrigin = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _defaultOrigin = defaultOrigin;
    }

    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "OAuth 2.0 PKCE redirect_uri formatting")]
    public string Build(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Priority 1: Explicit full callback URI (GOOGLE_REDIRECT_URI)
        var explicitUri = FirstNonEmpty(
            _config["Authentication:Google:RedirectUri"],
            _config["GOOGLE_REDIRECT_URI"],
            _config["GOOGLE_CALLBACK_URL"],
            Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI"),
            Environment.GetEnvironmentVariable("GOOGLE_CALLBACK_URL"))?.Trim();

        if (!string.IsNullOrWhiteSpace(explicitUri) &&
            Uri.TryCreate(explicitUri, UriKind.Absolute, out var parsedExplicit))
        {
            // Strip any accidental trailing slash so it matches Google Cloud Console strictly
            return parsedExplicit.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        // Priority 2: Combine Public Origin + Clean Path
        var normalizedPath = NormalizePath(
            FirstNonEmpty(
                _config["Authentication:Google:RedirectPath"],
                _config["GOOGLE_REDIRECT_PATH"],
                Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_PATH")));

        // Adjustable env vars: checks GOOGLE_PUBLIC_ORIGIN first, then NEXT_PUBLIC_APP_URL, APP_URL, Authentication:PublicAppUrl, _defaultOrigin
        var configuredOrigin =
            FirstNonEmpty(
                _config["GOOGLE_PUBLIC_ORIGIN"],
                _config["Authentication:PublicAppUrl"],
                _config["NEXT_PUBLIC_APP_URL"],
                _config["APP_URL"],
                Environment.GetEnvironmentVariable("GOOGLE_PUBLIC_ORIGIN"),
                Environment.GetEnvironmentVariable("NEXT_PUBLIC_APP_URL"),
                Environment.GetEnvironmentVariable("APP_URL"),
                _defaultOrigin)
            ?.Trim()
            .TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(configuredOrigin))
        {
            return $"{configuredOrigin}{normalizedPath}";
        }

        // Priority 3: Fallback to proxy-forwarded request headers
        var request = httpContext.Request;
        var scheme = string.Equals(
            request.Headers["X-Forwarded-Proto"].FirstOrDefault(),
            "https",
            StringComparison.OrdinalIgnoreCase)
            ? "https"
            : request.Scheme;

        if (string.IsNullOrWhiteSpace(scheme))
        {
            scheme = "http";
        }

        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault()?.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            host = request.Host.Value;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }

        return $"{scheme}://{host}{normalizedPath}";
    }

    /// <summary>
    /// Safely normalizes GOOGLE_REDIRECT_PATH even if a full URL was accidentally pasted into it.
    /// Example: "https://example.dev/api/auth/google/callback/" -> "/api/auth/google/callback"
    /// </summary>
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "URL path normalization")]
    public static string NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return DefaultCallbackPath;
        }

        var trimmed = rawPath.Trim();

        // Guard against full URL accidentally placed in GOOGLE_REDIRECT_PATH
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var fullUri))
        {
            trimmed = fullUri.AbsolutePath;
        }

        var cleaned = "/" + trimmed.Trim('/');
        return cleaned == "/" ? DefaultCallbackPath : cleaned;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
