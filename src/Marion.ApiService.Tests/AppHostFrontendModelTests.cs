#pragma warning disable ASPIRECERTIFICATES001

extern alias AppHost;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

[Collection(AppHostTestCollection.Name)]
public sealed class AppHostFrontendModelTests
{
    [Fact]
    public async Task Development_models_the_frontend_at_the_stable_HTTPS_origin()
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

        var frontend = Assert.Single(resources, resource => resource.Name == "frontend");
        var database = Assert.Single(resources, resource => resource.Name == "mariondb");
        var endpoint = Assert.Single(
            annotations[frontend].OfType<EndpointAnnotation>(),
            annotation => annotation.Name == "https");
        var certificate = Assert.Single(
            annotations[frontend].OfType<HttpsCertificateAnnotation>());

        Assert.Equal(7257, endpoint.Port);
        Assert.Equal("https", endpoint.UriScheme);
        Assert.True(endpoint.TlsEnabled);
        Assert.True(endpoint.IsExternal);
        Assert.True(certificate.UseDeveloperCertificate);
        Assert.Contains(
            annotations[frontend].OfType<ResourceRelationshipAnnotation>(),
            annotation => annotation.Resource == database
                && annotation.Type == "Reference");
        Assert.Contains(
            annotations[frontend].OfType<WaitAnnotation>(),
            annotation => annotation.Resource == database);
    }
}
