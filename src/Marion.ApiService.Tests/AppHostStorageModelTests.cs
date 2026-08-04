extern alias AppHost;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

[Collection(AppHostTestCollection.Name)]
public sealed class AppHostStorageModelTests
{
    [Fact]
    public async Task Development_models_persistent_private_document_storage()
    {
        await using var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>();

        var resources = builder.Resources.ToArray();
        var annotations = resources.ToDictionary(
            resource => resource,
            resource => resource.Annotations.ToArray());

        {
            await using var app = await builder.BuildAsync();
        }

        var storage = Assert.IsType<AzureStorageResource>(
            Assert.Single(resources, resource => resource.Name == "storage"));
        var documents = Assert.IsType<AzureBlobStorageContainerResource>(
            Assert.Single(resources, resource => resource.Name == "documents"));
        var apiService = Assert.Single(
            resources,
            resource => resource.Name == "apiservice");

        Assert.True(storage.IsEmulator);
        Assert.Equal("test-files", documents.BlobContainerName);
        Assert.Contains(
            annotations[storage].OfType<ContainerMountAnnotation>(),
            annotation => annotation.Type == ContainerMountType.Volume);
        Assert.Contains(
            annotations[storage].OfType<ContainerLifetimeAnnotation>(),
            annotation => annotation.Lifetime == ContainerLifetime.Persistent);
        Assert.Contains(
            annotations[apiService].OfType<ResourceRelationshipAnnotation>(),
            annotation => annotation.Resource == documents
                && annotation.Type == "Reference");
        Assert.Contains(
            annotations[apiService].OfType<WaitAnnotation>(),
            annotation => annotation.Resource == documents);
    }

    [Fact]
    public async Task IntegrationTesting_models_ephemeral_storage_with_dynamic_ports()
    {
        await using var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>(
                ["--IntegrationTesting=true"]);

        var resources = builder.Resources.ToArray();
        var annotations = resources.ToDictionary(
            resource => resource,
            resource => resource.Annotations.ToArray());

        {
            await using var app = await builder.BuildAsync();
        }

        var storage = Assert.IsType<AzureStorageResource>(
            Assert.Single(resources, resource => resource.Name == "storage"));
        var documents = Assert.IsType<AzureBlobStorageContainerResource>(
            Assert.Single(resources, resource => resource.Name == "documents"));
        var apiService = Assert.Single(
            resources,
            resource => resource.Name == "apiservice");

        Assert.True(storage.IsEmulator);
        Assert.Equal("test-files", documents.BlobContainerName);
        Assert.DoesNotContain(
            resources,
            resource => resource.Name == "frontend");
        Assert.DoesNotContain(
            annotations[storage],
            annotation => annotation is ContainerMountAnnotation);
        Assert.Contains(
            annotations[storage].OfType<ContainerLifetimeAnnotation>(),
            annotation => annotation.Lifetime == ContainerLifetime.Session);
        Assert.NotEmpty(annotations[storage].OfType<EndpointAnnotation>());
        Assert.All(
            annotations[storage].OfType<EndpointAnnotation>(),
            annotation => Assert.Null(annotation.Port));
        Assert.Contains(
            annotations[apiService].OfType<ResourceRelationshipAnnotation>(),
            annotation => annotation.Resource == documents
                && annotation.Type == "Reference");
        Assert.Contains(
            annotations[apiService].OfType<WaitAnnotation>(),
            annotation => annotation.Resource == documents);
    }
}
