using System.Security.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Rge.Accounting.Host.Configuration;

/// <summary>
/// Hardens Kestrel web server transport security, TLS protocols, and payload limits.
/// </summary>
public static class KestrelConfiguration
{
    public const long DefaultMaxRequestBodyBytes = 1_048_576; // 1 MB default (10 MB overridden on document upload endpoint)
    public const long MaxDocumentUploadBytes = 10_485_760;    // 10 MB cap per specification

    public static WebApplicationBuilder ConfigureHardenedKestrel(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WebHost.ConfigureKestrel(options =>
        {
            // Never broadcast server identity banner
            options.AddServerHeader = false;

            // Strict request size and slowloris DoS defense limits
            options.Limits.MaxRequestBodySize = DefaultMaxRequestBodyBytes;
            options.Limits.MaxRequestHeadersTotalSize = 32 * 1024; // 32 KB
            options.Limits.MaxRequestHeaderCount = 50;
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);

            options.ConfigureHttpsDefaults(https =>
            {
                https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            });
        });

        return builder;
    }
}

