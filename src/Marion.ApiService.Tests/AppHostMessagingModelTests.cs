extern alias AppHost;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

[Collection(AppHostTestCollection.Name)]
public sealed class AppHostMessagingModelTests
{
    [Fact]
    public async Task AppHost_models_the_Service_Bus_emulator_and_API_dependency_without_RBAC_assignments()
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

        var messaging = Assert.IsType<AzureServiceBusResource>(
            Assert.Single(resources, resource => resource.Name == "messaging"));
        var documentProcessing = Assert.IsType<AzureServiceBusQueueResource>(
            Assert.Single(resources, resource => resource.Name == "document-processing"));
        var loanEvents = Assert.IsType<AzureServiceBusTopicResource>(
            Assert.Single(resources, resource => resource.Name == "loan-events"));
        var loanEventsSubscription = Assert.IsType<AzureServiceBusSubscriptionResource>(
            Assert.Single(
                resources,
                resource => resource.Name == "loan-events-subscription"));
        var apiService = Assert.Single(
            resources,
            resource => resource.Name == "apiservice");

        Assert.True(messaging.IsEmulator);
        Assert.Same(messaging, documentProcessing.Parent);
        Assert.Same(messaging, loanEvents.Parent);
        Assert.Equal("loan-events-subscription", loanEventsSubscription.SubscriptionName);
        Assert.Same(loanEvents, loanEventsSubscription.Parent);
        Assert.DoesNotContain(
            annotations[messaging],
            annotation => annotation is DefaultRoleAssignmentsAnnotation);
        Assert.DoesNotContain(
            annotations[apiService],
            annotation => annotation is RoleAssignmentAnnotation);
        Assert.Contains(
            annotations[apiService].OfType<ResourceRelationshipAnnotation>(),
            annotation => annotation.Resource == messaging
                && annotation.Type == "Reference");
        Assert.Contains(
            annotations[apiService].OfType<WaitAnnotation>(),
            annotation => annotation.Resource == messaging);
    }
}
