using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marion.ApiService.Features.System;
using Marion.ApiService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Health_endpoints_return_a_documented_JSON_status(string path)
    {
        var response = await client.GetAsync(path);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(JsonValueKind.Object, payload.RootElement.ValueKind);
        Assert.Single(payload.RootElement.EnumerateObject());
        Assert.Equal("Healthy", payload.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(nameof(MarionDbContext), HealthStatus.Degraded, DependencyState.Degraded)]
    [InlineData(nameof(MarionDbContext), HealthStatus.Unhealthy, DependencyState.Unavailable)]
    [InlineData("documents", HealthStatus.Degraded, DependencyState.Degraded)]
    [InlineData("documents", HealthStatus.Unhealthy, DependencyState.Unavailable)]
    public async Task Dependencies_maps_unhealthy_states_without_exposing_diagnostics(
        string dependencyName,
        HealthStatus healthStatus,
        DependencyState expectedState)
    {
        using var statusFactory = new MarionApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck(
                    dependencyName,
                    () => new HealthCheckResult(
                        healthStatus,
                        "Data Source=internal;SensitiveValue=not-safe",
                        new InvalidOperationException("stack and connection details")))));
        using var statusClient = statusFactory.CreateClient();

        var response = await statusClient.GetAsync("/api/system/dependencies");
        var payload = await response.Content.ReadFromJsonAsync<SystemDependenciesResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Contains(payload.Dependencies, dependency =>
            dependency.Name == dependencyName
            && dependency.Status == expectedState);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Data Source", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-safe", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Blob_unavailability_preserves_readiness_liveness_and_safe_dependency_contracts()
    {
        using var statusFactory = new MarionApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck(
                    "documents",
                    () => HealthCheckResult.Unhealthy(
                        "Blob endpoint internal; storage credential redacted",
                        new InvalidOperationException("stack and connection details")))));
        using var statusClient = statusFactory.CreateClient();

        using var readyResponse = await statusClient.GetAsync("/health");
        using var liveResponse = await statusClient.GetAsync("/alive");
        using var dependenciesResponse =
            await statusClient.GetAsync("/api/system/dependencies");
        using var readyPayload =
            JsonDocument.Parse(await readyResponse.Content.ReadAsStreamAsync());
        using var livePayload =
            JsonDocument.Parse(await liveResponse.Content.ReadAsStreamAsync());
        var dependencies = await dependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Equal("application/json", readyResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Unhealthy", readyPayload.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal("application/json", liveResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", livePayload.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, dependenciesResponse.StatusCode);
        Assert.NotNull(dependencies);
        Assert.Contains(dependencies.Dependencies, dependency =>
            dependency.Name == "documents"
            && dependency.Status == DependencyState.Unavailable);

        var body = await dependenciesResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Blob endpoint", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redacted", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Service_Bus_unavailability_preserves_readiness_liveness_and_safe_dependency_contracts()
    {
        using var statusFactory = new MarionApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck(
                    "Azure_ServiceBusClient",
                    () => HealthCheckResult.Unhealthy(
                        "Service Bus endpoint and credential are private",
                        new InvalidOperationException("stack and connection details")))));
        using var statusClient = statusFactory.CreateClient();

        using var readyResponse = await statusClient.GetAsync("/health");
        using var liveResponse = await statusClient.GetAsync("/alive");
        using var dependenciesResponse =
            await statusClient.GetAsync("/api/system/dependencies");
        var dependencies = await dependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dependenciesResponse.StatusCode);
        Assert.NotNull(dependencies);
        Assert.Contains(dependencies.Dependencies, dependency =>
            dependency.Name == "Azure_ServiceBusClient"
            && dependency.Status == DependencyState.Unavailable);

        var body = await dependenciesResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Service Bus endpoint", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Synthetic_storage_route_is_not_exposed_in_Production()
    {
        using var productionFactory = new MarionApiFactory("Production");
        using var productionClient = productionFactory.CreateClient();

        var response = await productionClient.PostAsync(
            "/api/system/storage/verify",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Synthetic_messaging_route_is_not_exposed_in_Production()
    {
        using var productionFactory = new MarionApiFactory("Production");
        using var productionClient = productionFactory.CreateClient();

        var response = await productionClient.PostAsync(
            "/api/system/messaging/publish-synthetic",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
