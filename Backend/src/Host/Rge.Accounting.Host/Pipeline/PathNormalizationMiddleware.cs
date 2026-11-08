using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rge.Accounting.Host.Pipeline;

/// <summary>
/// Normalizes consecutive forward slashes in incoming HTTP request paths (e.g. //api/... -> /api/...)
/// and path bases in-place prior to endpoint routing, security policies, and request size limits.
/// Preserves the raw request body stream, headers, query string, and HTTP method for cryptographic
/// HMAC-SHA256 signature verification and downstream endpoint execution.
/// </summary>
public sealed class PathNormalizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;
    private readonly ILogger? _legacyLogger;

    /// <summary>
    /// Primary constructor for dependency injection with optional legacy logger support.
    /// </summary>
    /// <param name="next">The next middleware delegate in the HTTP pipeline.</param>
    /// <param name="logger">The primary logger instance for this middleware.</param>
    /// <param name="legacyLogger">Optional legacy logger instance to preserve backward-compatible log categories.</param>
    [ActivatorUtilitiesConstructor]
    public PathNormalizationMiddleware(
        RequestDelegate next,
        ILogger<PathNormalizationMiddleware> logger,
        ILogger<DuplicateSlashNormalizationMiddleware>? legacyLogger = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _legacyLogger = legacyLogger;
    }

    /// <summary>
    /// Overloaded constructor for non-generic or untyped logger instances.
    /// </summary>
    /// <param name="next">The next middleware delegate in the HTTP pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    public PathNormalizationMiddleware(
        RequestDelegate next,
        ILogger logger)
        : this(next, logger, (ILogger?)null)
    {
    }

    /// <summary>
    /// Internal constructor accepting dual logger instances for bridge delegations.
    /// </summary>
    /// <param name="next">The next middleware delegate in the HTTP pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="legacyLogger">Optional legacy logger instance.</param>
    internal PathNormalizationMiddleware(
        RequestDelegate next,
        ILogger logger,
        ILogger? legacyLogger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _legacyLogger = legacyLogger;
    }

    /// <summary>
    /// Normalizes incoming request path and path base in-place if duplicate slashes are detected.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the completion of request processing.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? pathValue = context.Request.Path.Value;
        string? pathBaseValue = context.Request.PathBase.Value;

        bool pathHasDuplicates = !string.IsNullOrEmpty(pathValue) && pathValue.Contains("//", StringComparison.Ordinal);
        bool pathBaseHasDuplicates = !string.IsNullOrEmpty(pathBaseValue) && pathBaseValue.Contains("//", StringComparison.Ordinal);

        // Fast-path guard: 99.99% of valid requests do not contain consecutive slashes.
        // Performs zero heap allocations when no consecutive slashes are present.
        if (!pathHasDuplicates && !pathBaseHasDuplicates)
        {
            return _next(context);
        }

        if (pathHasDuplicates)
        {
            string normalizedPath = CollapseDuplicateSlashes(pathValue!);
            context.Request.Path = new PathString(normalizedPath);

            LogPathNormalization(pathValue!, normalizedPath, context.Request.Method);
        }

        if (pathBaseHasDuplicates)
        {
            string normalizedPathBase = CollapseDuplicateSlashes(pathBaseValue!);
            context.Request.PathBase = new PathString(normalizedPathBase);

            LogPathBaseNormalization(pathBaseValue!, normalizedPathBase, context.Request.Method);
        }

        return _next(context);
    }

    /// <summary>
    /// Collapses consecutive forward slashes into a single slash in a single O(N) pass.
    /// Eliminates allocation DoS risks from nested string replacement loops.
    /// Handles edge cases:
    /// - Leading duplicate slashes: "//api/v1/..." -> "/api/v1/..."
    /// - Multiple internal slashes: "/api///v1////webhooks//..." -> "/api/v1/webhooks/..."
    /// - Root slashes: "//" -> "/", "///" -> "/"
    /// - Trailing duplicate slashes: "/path//" -> "/path/"
    /// - Single slash or clean paths: "/api/v1/..." returned as-is
    /// - Null or empty strings: returned as-is
    /// </summary>
    /// <param name="path">The input URL path to normalize.</param>
    /// <returns>The normalized path with all consecutive slashes collapsed to a single slash.</returns>
    public static string CollapseDuplicateSlashes(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        int firstDoubleSlash = path.IndexOf("//", StringComparison.Ordinal);
        if (firstDoubleSlash < 0)
        {
            return path;
        }

        var sb = new StringBuilder(path.Length);
        sb.Append(path, 0, firstDoubleSlash + 1);
        bool previousWasSlash = true;

        for (int i = firstDoubleSlash + 1; i < path.Length; i++)
        {
            char c = path[i];
            if (c == '/')
            {
                if (previousWasSlash)
                {
                    continue;
                }
                previousWasSlash = true;
            }
            else
            {
                previousWasSlash = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private void LogPathNormalization(string rawPath, string normalizedPath, string method)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "[PATH-NORMALIZATION] Normalized request path with duplicate slashes from '{OriginalPath}' to '{NormalizedPath}' for HTTP {Method}",
                rawPath,
                normalizedPath,
                method);
        }

        if (_legacyLogger is not null && !ReferenceEquals(_logger, _legacyLogger) && _legacyLogger.IsEnabled(LogLevel.Information))
        {
            _legacyLogger.LogInformation(
                "[PATH-NORMALIZATION] Normalized request path with duplicate slashes from '{OriginalPath}' to '{NormalizedPath}' for HTTP {Method}",
                rawPath,
                normalizedPath,
                method);
        }
    }

    private void LogPathBaseNormalization(string rawPathBase, string normalizedPathBase, string method)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "[PATH-NORMALIZATION] Normalized request pathBase with duplicate slashes from '{OriginalPathBase}' to '{NormalizedPathBase}' for HTTP {Method}",
                rawPathBase,
                normalizedPathBase,
                method);
        }

        if (_legacyLogger is not null && !ReferenceEquals(_logger, _legacyLogger) && _legacyLogger.IsEnabled(LogLevel.Information))
        {
            _legacyLogger.LogInformation(
                "[PATH-NORMALIZATION] Normalized request pathBase with duplicate slashes from '{OriginalPathBase}' to '{NormalizedPathBase}' for HTTP {Method}",
                rawPathBase,
                normalizedPathBase,
                method);
        }
    }
}
