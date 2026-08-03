using Azure.Core;
using Azure.Storage.Blobs;
using Marion.ApiService.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Marion.ApiService.Infrastructure.Storage;

internal static class DocumentStorageServiceCollectionExtensions
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(5);

    internal static IServiceCollection AddDocumentStorage(
        this IServiceCollection services,
        bool disableHealthChecks)
    {
        var aspireBlobClientRegistration = services.LastOrDefault(
            descriptor => descriptor.ServiceType == typeof(BlobContainerClient)
                && descriptor.ServiceKey is null)
            ?? throw new InvalidOperationException(
                "The Aspire documents Blob client must be registered before document storage.");
        services.Remove(aspireBlobClientRegistration);
        services.AddSingleton<BlobContainerClient>(serviceProvider =>
            CreateBlobContainerClient(serviceProvider, aspireBlobClientRegistration));
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

    private static BlobContainerClient CreateBlobContainerClient(
        IServiceProvider serviceProvider,
        ServiceDescriptor aspireBlobClientRegistration)
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<PlatformOptions>>()
            .Value;

        return options.Mode switch
        {
            PlatformMode.Local => ResolveAspireBlobContainerClient(
                serviceProvider,
                aspireBlobClientRegistration),
            PlatformMode.Azure => CreateAzureBlobContainerClient(
                options.Azure,
                serviceProvider.GetRequiredService<TokenCredential>()),
            _ => throw new InvalidOperationException(
                "A supported platform mode is required before registering document storage.")
        };
    }

    private static BlobContainerClient ResolveAspireBlobContainerClient(
        IServiceProvider serviceProvider,
        ServiceDescriptor registration)
    {
        if (registration.ImplementationFactory is not null)
        {
            return (BlobContainerClient)registration.ImplementationFactory(serviceProvider);
        }

        if (registration.ImplementationInstance is BlobContainerClient instance)
        {
            return instance;
        }

        return (BlobContainerClient)ActivatorUtilities.GetServiceOrCreateInstance(
            serviceProvider,
            registration.ImplementationType
                ?? throw new InvalidOperationException(
                    "The Aspire documents Blob client registration is invalid."));
    }

    private static BlobContainerClient CreateAzureBlobContainerClient(
        AzurePlatformOptions options,
        TokenCredential credential)
    {
        var clientOptions = new BlobClientOptions
        {
            Retry =
            {
                NetworkTimeout = ReadinessTimeout
            }
        };

        return new BlobServiceClient(
                new Uri(options.BlobServiceUri!),
                credential,
                clientOptions)
            .GetBlobContainerClient(options.BlobContainerName!);
    }
}
