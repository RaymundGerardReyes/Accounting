using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Rge.Accounting.Host.Pipeline;

/// <summary>
/// Backward-compatible bridge delegating to <see cref="PathNormalizationMiddleware"/>.
/// Maintained so that existing references, test suites, and components remain 100% backward compatible
/// without duplicate pipeline overhead.
/// </summary>
public sealed class DuplicateSlashNormalizationMiddleware
{
    private readonly PathNormalizationMiddleware _inner;

    /// <summary>
    /// Initializes a new instance of <see cref="DuplicateSlashNormalizationMiddleware"/> delegating to <see cref="PathNormalizationMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware delegate in the HTTP pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    public DuplicateSlashNormalizationMiddleware(
        RequestDelegate next,
        ILogger<DuplicateSlashNormalizationMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = new PathNormalizationMiddleware(next, logger, logger);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DuplicateSlashNormalizationMiddleware"/> delegating to <see cref="PathNormalizationMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware delegate in the HTTP pipeline.</param>
    /// <param name="logger">The primary logger instance for PathNormalizationMiddleware.</param>
    public DuplicateSlashNormalizationMiddleware(
        RequestDelegate next,
        ILogger<PathNormalizationMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = new PathNormalizationMiddleware(next, logger, null);
    }

    /// <summary>
    /// Invokes the underlying <see cref="PathNormalizationMiddleware"/>.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the completion of request processing.</returns>
    public Task InvokeAsync(HttpContext context) => _inner.InvokeAsync(context);

    /// <summary>
    /// Static delegate to <see cref="PathNormalizationMiddleware.CollapseDuplicateSlashes(string)"/>.
    /// </summary>
    /// <param name="path">The input URL path to normalize.</param>
    /// <returns>The normalized path with all consecutive slashes collapsed to a single slash.</returns>
    public static string CollapseDuplicateSlashes(string path) =>
        PathNormalizationMiddleware.CollapseDuplicateSlashes(path);
}
