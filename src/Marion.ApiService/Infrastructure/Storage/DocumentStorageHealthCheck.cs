using Azure;
using Azure.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marion.ApiService.Infrastructure.Storage;

internal sealed class DocumentStorageHealthCheck(IDocumentStorage storage)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.CheckReadinessAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "Document storage readiness check timed out.");
        }
        catch (AuthenticationFailedException)
        {
            return HealthCheckResult.Unhealthy(
                "Document storage is unavailable.");
        }
        catch (RequestFailedException)
        {
            return HealthCheckResult.Unhealthy(
                "Document storage is unavailable.");
        }
        catch (HttpRequestException)
        {
            return HealthCheckResult.Unhealthy(
                "Document storage is unavailable.");
        }
    }
}
