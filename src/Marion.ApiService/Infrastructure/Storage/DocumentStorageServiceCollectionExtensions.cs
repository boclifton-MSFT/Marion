using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marion.ApiService.Infrastructure.Storage;

internal static class DocumentStorageServiceCollectionExtensions
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(5);

    internal static IServiceCollection AddDocumentStorage(
        this IServiceCollection services,
        bool disableHealthChecks)
    {
        services.TryAddSingleton<IDocumentStorage, AzureBlobDocumentStorage>();
        services.TryAddSingleton<IDocumentStorageVerifier, DocumentStorageVerifier>();

        if (!disableHealthChecks)
        {
            services.AddHealthChecks()
                .AddCheck<DocumentStorageHealthCheck>(
                    "documents",
                    HealthStatus.Unhealthy,
                    tags: [],
                    timeout: ReadinessTimeout);
        }

        return services;
    }
}
