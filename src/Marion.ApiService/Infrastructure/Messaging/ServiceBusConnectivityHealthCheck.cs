using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marion.ApiService.Infrastructure.Messaging;

internal sealed class ServiceBusConnectivityHealthCheck(ServiceBusClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var receiver = client.CreateReceiver(
                MessagingEntityNames.DocumentProcessingQueue);
            await receiver.PeekMessageAsync(cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "Service Bus connectivity check timed out.");
        }
        catch (ServiceBusException)
        {
            return HealthCheckResult.Unhealthy(
                "Service Bus connectivity check failed.");
        }
        catch (TimeoutException)
        {
            return HealthCheckResult.Unhealthy(
                "Service Bus connectivity check timed out.");
        }
    }
}
