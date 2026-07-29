using System.Net;
using System.Net.Http.Json;
using Marion.ApiService.Features.System;
using Marion.ApiService.Infrastructure.Persistence;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class SystemEndpointsTests : IClassFixture<MarionApiFactory>
{
    private readonly HttpClient client;

    public SystemEndpointsTests(MarionApiFactory factory)
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
        SensitiveOutputAssertions.DoesNotContainSensitiveDetails(body);
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
        SensitiveOutputAssertions.DoesNotContainSensitiveDetails(body);
    }

    [Fact]
    public async Task SQL_unavailability_fails_readiness_without_failing_liveness()
    {
        using var unavailableFactory = new MarionApiFactory("Development");
        using var unavailableClient = unavailableFactory.CreateClient();

        using var readinessResponse = await unavailableClient.GetAsync("/health");
        using var livenessResponse = await unavailableClient.GetAsync("/alive");
        using var dependenciesResponse = await unavailableClient.GetAsync("/api/system/dependencies");
        var payload = await dependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readinessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, livenessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dependenciesResponse.StatusCode);
        Assert.NotNull(payload);
        Assert.Contains(payload.Dependencies, dependency =>
            dependency.Name == nameof(MarionDbContext)
            && dependency.Status == DependencyState.Unavailable);

        var body = await dependenciesResponse.Content.ReadAsStringAsync();
        SensitiveOutputAssertions.DoesNotContainSensitiveDetails(body);
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
