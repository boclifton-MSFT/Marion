using Azure;
using Marion.ApiService.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class DocumentStorageHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_returns_a_sanitized_unhealthy_result_for_request_failures()
    {
        var healthCheck = new DocumentStorageHealthCheck(
            new FailingDocumentStorage(new RequestFailedException("secret endpoint details")));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Document storage is unavailable.", result.Description);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task CheckHealth_returns_a_bounded_timeout_result_when_readiness_is_canceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var healthCheck = new DocumentStorageHealthCheck(
            new FailingDocumentStorage(new OperationCanceledException(cancellation.Token)));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            cancellation.Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            "Document storage readiness check timed out.",
            result.Description);
        Assert.Null(result.Exception);
    }

    private sealed class FailingDocumentStorage(Exception failure) : IDocumentStorage
    {
        public Task UploadAsync(
            string blobName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<byte[]> DownloadAsync(
            string blobName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Array.Empty<byte>());

        public Task DeleteIfExistsAsync(
            string blobName,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CheckReadinessAsync(CancellationToken cancellationToken) =>
            Task.FromException(failure);
    }
}
