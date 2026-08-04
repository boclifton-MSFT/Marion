using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Marion.ApiService.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class StorageRegistrationTests
{
    [Fact]
    public void Development_registers_the_container_client_services_and_one_readiness_check()
    {
        using var factory = new MarionApiFactory("Development");
        using var scope = factory.Services.CreateScope();

        var containerClient =
            scope.ServiceProvider.GetRequiredService<BlobContainerClient>();
        var storage = scope.ServiceProvider.GetRequiredService<IDocumentStorage>();
        var verifier =
            scope.ServiceProvider.GetRequiredService<IDocumentStorageVerifier>();
        var registrations = scope.ServiceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        var documentsRegistration =
            Assert.Single(registrations, registration => registration.Name == "documents");

        Assert.Equal("test-files", containerClient.Name);
        Assert.Equal("https://storage.invalid/test-files", containerClient.Uri.AbsoluteUri);
        Assert.IsType<AzureBlobDocumentStorage>(storage);
        Assert.IsType<DocumentStorageVerifier>(verifier);
        Assert.DoesNotContain("live", documentsRegistration.Tags);
        Assert.Equal(HealthStatus.Unhealthy, documentsRegistration.FailureStatus);
        Assert.Equal(TimeSpan.FromSeconds(5), documentsRegistration.Timeout);
    }

    [Fact]
    public void Testing_keeps_fast_API_tests_independent_from_Azurite()
    {
        using var factory = new MarionApiFactory();
        var registrations = factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.DoesNotContain(
            registrations,
            registration => registration.Name == "documents");
        Assert.NotNull(factory.Services.GetRequiredService<BlobContainerClient>());
        Assert.NotNull(factory.Services.GetRequiredService<IDocumentStorage>());
        Assert.NotNull(factory.Services.GetRequiredService<IDocumentStorageVerifier>());
    }

    [Fact]
    public void Azure_mode_keeps_operational_blob_timeout_separate_from_readiness_timeout()
    {
        using var factory = new MarionApiFactory("Development").WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Marion:Platform:Mode", "Azure");
            builder.UseSetting(
                "Marion:Platform:Azure:BlobServiceUri",
                "https://documents.blob.core.windows.net");
            builder.UseSetting(
                "Marion:Platform:Azure:BlobContainerName",
                "documents");
            builder.UseSetting(
                "Marion:Platform:Azure:ServiceBusFullyQualifiedNamespace",
                "messaging.servicebus.windows.net");
            builder.UseSetting(
                "Marion:Platform:Azure:SqlServer",
                "marion.database.windows.net");
            builder.UseSetting(
                "Marion:Platform:Azure:SqlDatabase",
                "marion");
            builder.UseSetting(
                "Marion:Platform:Azure:Identity:TenantId",
                "tenant-id");
            builder.UseSetting(
                "ConnectionStrings:documents",
                "Endpoint=https://fallback.invalid;ContainerName=fallback");
        });

        using var scope = factory.Services.CreateScope();
        var containerClient = scope.ServiceProvider
            .GetRequiredService<BlobContainerClient>();
        var credential = scope.ServiceProvider
            .GetRequiredService<TokenCredential>();
        var healthRegistration = Assert.Single(
            scope.ServiceProvider
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value
                .Registrations,
            registration => registration.Name == "documents");
        var operationalOptions =
            DocumentStorageServiceCollectionExtensions.CreateOperationalBlobClientOptions();

        Assert.Equal(
            "https://documents.blob.core.windows.net/documents",
            containerClient.Uri.AbsoluteUri);
        Assert.IsType<ManagedIdentityCredential>(credential);
        Assert.Same(
            credential,
            scope.ServiceProvider.GetRequiredService<ManagedIdentityCredential>());
        Assert.Null(scope.ServiceProvider.GetService<DefaultAzureCredential>());
        Assert.Equal(TimeSpan.FromSeconds(5), healthRegistration.Timeout);
        Assert.Equal(
            new BlobClientOptions().Retry.NetworkTimeout,
            operationalOptions.Retry.NetworkTimeout);
        Assert.NotEqual(healthRegistration.Timeout, operationalOptions.Retry.NetworkTimeout);
    }
}
