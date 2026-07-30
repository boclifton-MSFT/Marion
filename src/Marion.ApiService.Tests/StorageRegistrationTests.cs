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
}
