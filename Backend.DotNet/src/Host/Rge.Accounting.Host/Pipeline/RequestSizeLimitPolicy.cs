using Microsoft.AspNetCore.Http.Features;

using Rge.Accounting.Host.Configuration;

using Rge.BuildingBlocks.Api.Http;

using Rge.BuildingBlocks.Api.Serialization;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.Accounting.Host.Pipeline;



/// <summary>

/// Enforces per-route payload size caps:

/// - 256 KB strict cap on PayMongo webhooks (prevents memory exhaustion prior to HMAC hashing)

/// - 10 MB cap on /api/v1/finance/documents uploads

/// - 1 MB default cap on all other JSON API endpoints

/// </summary>

public sealed class RequestSizeLimitPolicy

{

    public const long MaxWebhookPayloadBytes = 262_144; // 256 KB



    private readonly RequestDelegate _next;



    public RequestSizeLimitPolicy(RequestDelegate next)

    {

        _next = next;

    }



    public async Task InvokeAsync(HttpContext context)

    {

        ArgumentNullException.ThrowIfNull(context);



        PathString path = context.Request.Path;

        long allowedBytes = ResolveMaxBytesForPath(path);



        IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (sizeFeature is { IsReadOnly: false })

        {

            sizeFeature.MaxRequestBodySize = allowedBytes;

        }



        if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > allowedBytes)

        {

            var error = Error.Validation(

                "Security.PayloadTooLarge",

                $"Request payload exceeds maximum allowed size of {allowedBytes:N0} bytes for this route.");

            var problem = ProblemDetailsFactory.Create(context, error);

            problem.Status = StatusCodes.Status413PayloadTooLarge;

            problem.Title = "Payload Too Large";



            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

            await context.Response

                .WriteAsJsonAsync(problem, AppJsonContext.Default.ProblemDetails, contentType: "application/problem+json", cancellationToken: context.RequestAborted)

                .ConfigureAwait(false);

            return;

        }



        await _next(context).ConfigureAwait(false);

    }



    private static long ResolveMaxBytesForPath(PathString path)

    {

        if (path.StartsWithSegments("/api/v1/webhooks", StringComparison.OrdinalIgnoreCase))

        {

            return MaxWebhookPayloadBytes;

        }



        if (path.StartsWithSegments("/api/v1/finance/documents", StringComparison.OrdinalIgnoreCase))

        {

            return KestrelConfiguration.MaxDocumentUploadBytes;

        }



        return KestrelConfiguration.DefaultMaxRequestBodyBytes;

    }

}
