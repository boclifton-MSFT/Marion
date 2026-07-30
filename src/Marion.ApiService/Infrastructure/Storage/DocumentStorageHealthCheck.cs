using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marion.ApiService.Infrastructure.Storage;

internal sealed class DocumentStorageHealthCheck(IDocumentStorage storage)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await storage.CheckReadinessAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
