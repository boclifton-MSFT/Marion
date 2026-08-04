using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http;

namespace Marion.ApiService.Infrastructure.Messaging;

internal sealed class ServiceBusConnectivityHealthCheck(ServiceBusSender sender) : IHealthCheck
{
    private const string UnavailableDescription = "Service Bus connectivity check failed.";
    private const string TimeoutDescription = "Service Bus connectivity check timed out.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Creating the batch opens the sender link and obtains broker limits without sending a message.
            using var batch = await sender.CreateMessageBatchAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(TimeoutDescription);
        }
        catch (AuthenticationFailedException)
        {
            return HealthCheckResult.Unhealthy(UnavailableDescription);
        }
        catch (UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy(UnavailableDescription);
        }
        catch (ServiceBusException)
        {
            return HealthCheckResult.Unhealthy(UnavailableDescription);
        }
        catch (RequestFailedException)
        {
            return HealthCheckResult.Unhealthy(UnavailableDescription);
        }
        catch (HttpRequestException)
        {
            return HealthCheckResult.Unhealthy(UnavailableDescription);
        }
        catch (TimeoutException)
        {
            return HealthCheckResult.Unhealthy(TimeoutDescription);
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy(UnavailableDescription);
        }
    }
}
