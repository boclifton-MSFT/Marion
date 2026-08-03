extern alias AppHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Marion.ApiService.Features.System;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

public sealed class BlobStorageIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Document_storage_round_trip_outage_and_recovery_are_safe_and_isolated()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>(
                MarionApiFactory.IntegrationTestingArguments,
                timeout.Token);

        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("storage", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("documents", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);

        using var apiClient = app.CreateHttpClient("apiservice", "http");
        apiClient.Timeout = TimeSpan.FromSeconds(15);

        var documentsConnectionString =
            await app.GetConnectionStringAsync("documents", timeout.Token);
        Assert.False(string.IsNullOrWhiteSpace(documentsConnectionString));
        var containerClient = CreateContainerClient(documentsConnectionString);

        var accessPolicy = await containerClient.GetAccessPolicyAsync(
            cancellationToken: timeout.Token);
        Assert.Equal(PublicAccessType.None, accessPolicy.Value.BlobPublicAccess);
        await AssertNoSyntheticBlobsAsync(containerClient, timeout.Token);

        using var verificationResponse = await apiClient.PostAsync(
            "/api/system/storage/verify",
            content: null,
            timeout.Token);
        var verification = await verificationResponse.Content
            .ReadFromJsonAsync<StorageVerificationResponse>(timeout.Token);

        Assert.Equal(HttpStatusCode.OK, verificationResponse.StatusCode);
        Assert.NotNull(verification);
        Assert.Equal("Healthy", verification.Status);
        Assert.True(verification.DurationMilliseconds >= 0);
        await AssertNoSyntheticBlobsAsync(containerClient, timeout.Token);

        using var healthyResponse = await apiClient.GetAsync("/health", timeout.Token);
        using var liveResponse = await apiClient.GetAsync("/alive", timeout.Token);
        using var healthyDependenciesResponse =
            await apiClient.GetAsync("/api/system/dependencies", timeout.Token);
        var healthyDependencies = await healthyDependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>(timeout.Token);

        await AssertStatusJsonAsync(
            healthyResponse,
            HttpStatusCode.OK,
            "Healthy",
            timeout.Token);
        await AssertStatusJsonAsync(
            liveResponse,
            HttpStatusCode.OK,
            "Healthy",
            timeout.Token);
        Assert.Equal(HttpStatusCode.OK, healthyDependenciesResponse.StatusCode);
        Assert.NotNull(healthyDependencies);
        Assert.Contains(healthyDependencies.Dependencies, dependency =>
            dependency.Name == "documents"
            && dependency.Status == DependencyState.Healthy);

        var stopResult = await app.ResourceCommands.ExecuteCommandAsync(
            "storage",
            KnownResourceCommands.StopCommand,
            timeout.Token);
        Assert.True(stopResult.Success);
        await app.ResourceNotifications.WaitForResourceAsync(
            "storage",
            KnownResourceStates.Exited,
            timeout.Token);

        using var unavailableResponse =
            await apiClient.GetAsync("/health", timeout.Token);
        using var stillLiveResponse =
            await apiClient.GetAsync("/alive", timeout.Token);
        using var unavailableDependenciesResponse =
            await apiClient.GetAsync("/api/system/dependencies", timeout.Token);
        var unavailableDependencies = await unavailableDependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>(timeout.Token);

        await AssertStatusJsonAsync(
            unavailableResponse,
            HttpStatusCode.ServiceUnavailable,
            "Unhealthy",
            timeout.Token);
        await AssertStatusJsonAsync(
            stillLiveResponse,
            HttpStatusCode.OK,
            "Healthy",
            timeout.Token);
        Assert.Equal(HttpStatusCode.OK, unavailableDependenciesResponse.StatusCode);
        Assert.NotNull(unavailableDependencies);
        Assert.Contains(unavailableDependencies.Dependencies, dependency =>
            dependency.Name == "documents"
            && dependency.Status == DependencyState.Unavailable);
        await AssertSanitizedAsync(unavailableDependenciesResponse, timeout.Token);

        var startResult = await app.ResourceCommands.ExecuteCommandAsync(
            "storage",
            KnownResourceCommands.StartCommand,
            timeout.Token);
        Assert.True(startResult.Success);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("storage", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("documents", timeout.Token);

        using var recoveredResponse = await apiClient.GetAsync("/health", timeout.Token);
        using var recoveredDependenciesResponse =
            await apiClient.GetAsync("/api/system/dependencies", timeout.Token);
        var recoveredDependencies = await recoveredDependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>(timeout.Token);

        await AssertStatusJsonAsync(
            recoveredResponse,
            HttpStatusCode.OK,
            "Healthy",
            timeout.Token);
        Assert.NotNull(recoveredDependencies);
        Assert.Contains(recoveredDependencies.Dependencies, dependency =>
            dependency.Name == "documents"
            && dependency.Status == DependencyState.Healthy);
        await AssertNoSyntheticBlobsAsync(containerClient, timeout.Token);
    }

    private static BlobContainerClient CreateContainerClient(string connectionString)
    {
        var segments = connectionString.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var containerNameSegment = Assert.Single(
            segments,
            segment => segment.StartsWith(
                "ContainerName=",
                StringComparison.OrdinalIgnoreCase));
        var containerName = containerNameSegment["ContainerName=".Length..];
        var storageConnectionString = string.Join(
            ';',
            segments.Where(segment => !string.Equals(
                segment,
                containerNameSegment,
                StringComparison.Ordinal)));

        return new BlobContainerClient(
            storageConnectionString,
            containerName,
            new BlobClientOptions
            {
                Retry =
                {
                    MaxRetries = 0,
                    NetworkTimeout = TimeSpan.FromSeconds(3)
                }
            });
    }

    private static async Task AssertNoSyntheticBlobsAsync(
        BlobContainerClient containerClient,
        CancellationToken cancellationToken)
    {
        var syntheticBlobs = new List<string>();

        await foreach (var blob in containerClient.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: "synthetic/",
            cancellationToken: cancellationToken))
        {
            syntheticBlobs.Add(blob.Name);
        }

        Assert.Empty(syntheticBlobs);
    }

    private static async Task AssertStatusJsonAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedHealthStatus,
        CancellationToken cancellationToken)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(JsonValueKind.Object, payload.RootElement.ValueKind);
        Assert.Single(payload.RootElement.EnumerateObject());
        Assert.Equal(
            expectedHealthStatus,
            payload.RootElement.GetProperty("status").GetString());
    }

    private static async Task AssertSanitizedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.DoesNotContain("BlobEndpoint", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountName", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }
}
