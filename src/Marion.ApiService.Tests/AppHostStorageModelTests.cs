extern alias AppHost;

using Aspire.Hosting;
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
    public async Task Run_model_selects_Local_platform_mode_without_Azure_deployment_parameters()
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

        var runEnvironment = await ResolveEnvironmentAsync(
            apiService,
            annotations[apiService],
            DistributedApplicationOperation.Run);
        Assert.Equal("Local", runEnvironment["Marion__Platform__Mode"]);
        Assert.DoesNotContain(
            resources,
            resource => resource.Name is "azure-sql-server" or "azure-sql-database");
    }

    [Fact]
    public async Task Publish_model_supplies_the_complete_Azure_platform_environment_contract()
    {
        await using var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>(
                ["--publisher", "manifest"]);

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
        var messaging = Assert.IsType<AzureServiceBusResource>(
            Assert.Single(resources, resource => resource.Name == "messaging"));
        var azureSqlServer = Assert.IsType<ParameterResource>(
            Assert.Single(resources, resource => resource.Name == "azure-sql-server"));
        var azureSqlDatabase = Assert.IsType<ParameterResource>(
            Assert.Single(resources, resource => resource.Name == "azure-sql-database"));
        var apiService = Assert.Single(
            resources,
            resource => resource.Name == "apiservice");

        Assert.False(storage.IsEmulator);
        Assert.False(messaging.IsEmulator);
        Assert.False(azureSqlServer.Secret);
        Assert.False(azureSqlDatabase.Secret);

        var environment = await ResolveEnvironmentAsync(
            apiService,
            annotations[apiService],
            DistributedApplicationOperation.Publish);

        Assert.Equal(6, environment.Count);
        Assert.Equal("Azure", environment["Marion__Platform__Mode"]);
        Assert.Equal(
            storage.BlobUriExpression.ValueExpression,
            GetManifestExpression(
                environment,
                "Marion__Platform__Azure__BlobServiceUri"));
        Assert.Equal(
            documents.BlobContainerName,
            environment["Marion__Platform__Azure__BlobContainerName"]);
        Assert.Equal(
            messaging.HostName.ValueExpression,
            GetManifestExpression(
                environment,
                "Marion__Platform__Azure__ServiceBusFullyQualifiedNamespace"));
        Assert.Equal(
            azureSqlServer.ValueExpression,
            GetManifestExpression(
                environment,
                "Marion__Platform__Azure__SqlServer"));
        Assert.Equal(
            azureSqlDatabase.ValueExpression,
            GetManifestExpression(
                environment,
                "Marion__Platform__Azure__SqlDatabase"));
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

    private static async Task<Dictionary<string, object>> ResolveEnvironmentAsync(
        IResource resource,
        IEnumerable<IResourceAnnotation> annotations,
        DistributedApplicationOperation operation)
    {
        foreach (var annotation in annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            var environment = new Dictionary<string, object>();
            var context = new EnvironmentCallbackContext(
                new DistributedApplicationExecutionContext(operation),
                resource,
                environment);
            await annotation.Callback(context);

            if (environment.ContainsKey("Marion__Platform__Mode"))
            {
                return environment;
            }
        }

        throw new InvalidOperationException(
            "The API platform mode environment callback is missing.");
    }

    private static string GetManifestExpression(
        IReadOnlyDictionary<string, object> environment,
        string name) =>
        Assert.IsAssignableFrom<IManifestExpressionProvider>(environment[name])
            .ValueExpression;
}
