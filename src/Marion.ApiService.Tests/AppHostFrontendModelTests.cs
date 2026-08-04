#pragma warning disable ASPIRECERTIFICATES001

extern alias AppHost;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

public sealed class AppHostFrontendModelTests
{
    [Fact]
    public async Task Development_models_the_frontend_at_the_stable_HTTPS_origin()
    {
        await using var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>();

        var frontend = Assert.Single(builder.Resources, resource => resource.Name == "frontend");
        var database = Assert.Single(builder.Resources, resource => resource.Name == "mariondb");
        var endpoint = Assert.Single(
            frontend.Annotations.OfType<EndpointAnnotation>(),
            annotation => annotation.Name == "https");
        var certificate = Assert.Single(frontend.Annotations.OfType<HttpsCertificateAnnotation>());

        Assert.Equal(7257, endpoint.Port);
        Assert.Equal("https", endpoint.UriScheme);
        Assert.True(endpoint.TlsEnabled);
        Assert.True(endpoint.IsExternal);
        Assert.True(certificate.UseDeveloperCertificate);
        Assert.Contains(
            frontend.Annotations.OfType<ResourceRelationshipAnnotation>(),
            annotation => annotation.Resource == database
                && annotation.Type == "Reference");
        Assert.Contains(
            frontend.Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource == database);
    }
}
