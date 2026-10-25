using Microsoft.Extensions.DependencyInjection;
using Rge.Documents.Application.DownloadDocument;
using Rge.Documents.Application.ListDocuments;
using Rge.Documents.Application.Ports;
using Rge.Documents.Application.UploadDocument;

namespace Rge.Documents.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMagicByteInspector, MagicByteInspectorAdapter>();
        services.AddSingleton<IAntivirusScanner, AntivirusScanHook>();
        services.AddSingleton<IDocumentVaultStore, SafeFileStore>();

        services.AddScoped<UploadDocumentHandler>();
        services.AddScoped<ListDocumentsHandler>();
        services.AddScoped<DownloadDocumentHandler>();

        return services;
    }
}

