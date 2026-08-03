extern alias AppHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Marion.ApiService.Features.System;
using Marion.ApiService.Infrastructure.Persistence;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

public sealed class SqlReadinessIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Readiness_tracks_SQL_while_liveness_and_safe_dependencies_remain_available()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>(
            ["--IntegrationTesting=true"],
            timeout.Token);

        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("sql", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);

        using var healthyClient = app.CreateHttpClient("apiservice", "http");
        healthyClient.Timeout = TimeSpan.FromSeconds(15);

        using var readyResponse = await healthyClient.GetAsync("/health", timeout.Token);
        using var liveResponse = await healthyClient.GetAsync("/alive", timeout.Token);
        using var healthyDependenciesResponse =
            await healthyClient.GetAsync("/api/system/dependencies", timeout.Token);
        var healthyDependencies = await healthyDependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>(timeout.Token);

        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        await AssertStatusOnlyJsonAsync(
            readyResponse,
            "Healthy",
            timeout.Token);
        await AssertStatusOnlyJsonAsync(
            liveResponse,
            "Healthy",
            timeout.Token);
        Assert.Equal(HttpStatusCode.OK, healthyDependenciesResponse.StatusCode);
        Assert.NotNull(healthyDependencies);
        Assert.Contains(healthyDependencies.Dependencies, dependency =>
            dependency.Name == nameof(MarionDbContext)
            && dependency.Status == DependencyState.Healthy);
        healthyClient.Dispose();

        var stopResult = await app.ResourceCommands.ExecuteCommandAsync(
            "sql",
            KnownResourceCommands.StopCommand,
            timeout.Token);
        Assert.True(stopResult.Success);

        using var unavailableClient = app.CreateHttpClient("apiservice", "http");
        unavailableClient.Timeout = TimeSpan.FromSeconds(15);

        using var stillLiveResponse = await unavailableClient.GetAsync("/alive", timeout.Token);
        using var unavailableResponse = await WaitForStatusAsync(
            unavailableClient,
            "/health",
            HttpStatusCode.ServiceUnavailable,
            timeout.Token);
        using var unavailableDependenciesResponse =
            await unavailableClient.GetAsync("/api/system/dependencies", timeout.Token);
        var unavailableDependencies = await unavailableDependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>(timeout.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillLiveResponse.StatusCode);
        await AssertStatusOnlyJsonAsync(
            unavailableResponse,
            "Unhealthy",
            timeout.Token);
        await AssertStatusOnlyJsonAsync(
            stillLiveResponse,
            "Healthy",
            timeout.Token);
        Assert.Equal(HttpStatusCode.OK, unavailableDependenciesResponse.StatusCode);
        Assert.NotNull(unavailableDependencies);
        Assert.Contains(unavailableDependencies.Dependencies, dependency =>
            dependency.Name == nameof(MarionDbContext)
            && dependency.Status == DependencyState.Unavailable);

        var body = await unavailableDependenciesResponse.Content.ReadAsStringAsync(timeout.Token);
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpResponseMessage> WaitForStatusAsync(
        HttpClient client,
        string path,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.GetAsync(path, cancellationToken);
            if (response.StatusCode == expectedStatus)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return await client.GetAsync(path, cancellationToken);
    }

    private static async Task AssertStatusOnlyJsonAsync(
        HttpResponseMessage response,
        string expectedHealthStatus,
        CancellationToken cancellationToken)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        Assert.Equal(JsonValueKind.Object, payload.RootElement.ValueKind);
        Assert.Single(payload.RootElement.EnumerateObject());
        Assert.Equal(expectedHealthStatus, payload.RootElement.GetProperty("status").GetString());
    }
}
