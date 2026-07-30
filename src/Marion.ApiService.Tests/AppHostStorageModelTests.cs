extern alias AppHost;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

public sealed class AppHostStorageModelTests
{
    [Fact]
    public async Task Development_models_persistent_private_document_storage()
    {
        var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>();

        var storage = Assert.IsType<AzureStorageResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "storage"));
        var documents = Assert.IsType<AzureBlobStorageContainerResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "documents"));
        var apiService = Assert.Single(
            builder.Resources,
            resource => resource.Name == "apiservice");

        Assert.True(storage.IsEmulator);
        Assert.Equal("test-files", documents.BlobContainerName);
        Assert.Contains(
            storage.Annotations.OfType<ContainerMountAnnotation>(),
            annotation => annotation.Type == ContainerMountType.Volume);
        Assert.Contains(
            storage.Annotations.OfType<ContainerLifetimeAnnotation>(),
            annotation => annotation.Lifetime == ContainerLifetime.Persistent);
        Assert.Contains(
            apiService.Annotations.OfType<ResourceRelationshipAnnotation>(),
            annotation => annotation.Resource == documents
                && annotation.Type == "Reference");
        Assert.Contains(
            apiService.Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource == documents);
    }

    [Fact]
    public async Task IntegrationTesting_models_ephemeral_storage_with_dynamic_ports()
    {
        var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>(
                ["--IntegrationTesting=true"]);

        var storage = Assert.IsType<AzureStorageResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "storage"));
        var documents = Assert.IsType<AzureBlobStorageContainerResource>(
            Assert.Single(builder.Resources, resource => resource.Name == "documents"));
        var apiService = Assert.Single(
            builder.Resources,
            resource => resource.Name == "apiservice");

        Assert.True(storage.IsEmulator);
        Assert.Equal("test-files", documents.BlobContainerName);
        Assert.DoesNotContain(builder.Resources, resource => resource.Name == "frontend");
        Assert.DoesNotContain(
            storage.Annotations,
            annotation => annotation is ContainerMountAnnotation);
        Assert.Contains(
            storage.Annotations.OfType<ContainerLifetimeAnnotation>(),
            annotation => annotation.Lifetime == ContainerLifetime.Session);
        Assert.NotEmpty(storage.Annotations.OfType<EndpointAnnotation>());
        Assert.All(
            storage.Annotations.OfType<EndpointAnnotation>(),
            annotation => Assert.Null(annotation.Port));
        Assert.Contains(
            apiService.Annotations.OfType<ResourceRelationshipAnnotation>(),
            annotation => annotation.Resource == documents
                && annotation.Type == "Reference");
        Assert.Contains(
            apiService.Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource == documents);
    }
}
