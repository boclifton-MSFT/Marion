using System.Net;
using System.Net.Http.Json;
using Marion.ApiService.Features.System;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class SystemEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public SystemEndpointsTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Info_returns_safe_application_metadata()
    {
        var response = await client.GetAsync("/api/system/info");
        var payload = await response.Content.ReadFromJsonAsync<SystemInfoResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("Marion.ApiService", payload.ApplicationName);
        Assert.False(string.IsNullOrWhiteSpace(payload.Version));
        Assert.False(string.IsNullOrWhiteSpace(payload.Environment));
        Assert.Equal(DateTimeKind.Utc, payload.UtcTime.UtcDateTime.Kind);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dependencies_returns_safe_health_states()
    {
        var response = await client.GetAsync("/api/system/dependencies");
        var payload = await response.Content.ReadFromJsonAsync<SystemDependenciesResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.NotEmpty(payload.Dependencies);
        Assert.Contains(payload.Dependencies, dependency =>
            dependency.Name == "self" && dependency.Status == DependencyState.Healthy);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Weather_sample_is_removed()
    {
        var response = await client.GetAsync("/weatherforecast");
        var rootBody = await client.GetStringAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("weather", rootBody, StringComparison.OrdinalIgnoreCase);
    }
}
