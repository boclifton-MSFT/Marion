using Aspire.Azure.Messaging.ServiceBus;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Marion.ApiService.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Azure;

namespace Marion.ApiService.Infrastructure.Messaging;

internal static class MessagingServiceCollectionExtensions
{
    internal const string ServiceBusHealthCheckName = "Azure_ServiceBusClient";
    internal static readonly TimeSpan ConnectivityTimeout = TimeSpan.FromSeconds(4);
    internal static readonly TimeSpan HealthCheckRegistrationTimeout = TimeSpan.FromSeconds(5);

    internal static IHostApplicationBuilder AddPlatformIntegrationPublisher(
        this IHostApplicationBuilder builder)
    {
        var platformMode = PlatformConfigurationExtensions.ParseMode(
            builder.Configuration[PlatformOptions.SectionName + ":Mode"]);

        builder.AddAzureServiceBusClient(
            "messaging",
            settings =>
            {
                if (platformMode == PlatformMode.Azure)
                {
                    settings.ConnectionString = null;
                    settings.FullyQualifiedNamespace = builder.Configuration[
                        PlatformOptions.SectionName
                        + ":Azure:ServiceBusFullyQualifiedNamespace"];
                }

                if (!builder.Environment.IsEnvironment("Testing"))
                {
                    settings.HealthCheckQueueName = MessagingEntityNames.DocumentProcessingQueue;
                }
            },
            clientBuilder =>
            {
                if (platformMode == PlatformMode.Azure)
                {
                    clientBuilder.WithCredential(
                        serviceProvider => serviceProvider.GetRequiredService<TokenCredential>());
                }
            });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IServiceBusProbeTimer>(serviceProvider =>
            new ServiceBusProbeTimer(serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.TryAddSingleton<ServiceBusConnectivityHealthCheck>(serviceProvider =>
            new ServiceBusConnectivityHealthCheck(
                () => serviceProvider
                    .GetRequiredService<ServiceBusClient>()
                    .CreateSender(MessagingEntityNames.DocumentProcessingQueue),
                ConnectivityTimeout,
                serviceProvider.GetRequiredService<IServiceBusProbeTimer>()));
        builder.Services.TryAddSingleton<IPlatformIntegrationPublisher,
            AzureServiceBusPlatformIntegrationPublisher>();
        builder.Services.PostConfigure<HealthCheckServiceOptions>(ConfigureHealthCheck);

        return builder;
    }

    private static void ConfigureHealthCheck(HealthCheckServiceOptions options)
    {
        var registration = options.Registrations.FirstOrDefault(
            registration => string.Equals(
                registration.Name,
                ServiceBusHealthCheckName,
                StringComparison.Ordinal));
        if (registration is null)
        {
            return;
        }

        options.Registrations.Remove(registration);
        options.Registrations.Add(new HealthCheckRegistration(
            ServiceBusHealthCheckName,
            new Func<IServiceProvider, IHealthCheck>(serviceProvider =>
                serviceProvider.GetRequiredService<ServiceBusConnectivityHealthCheck>()),
            HealthStatus.Unhealthy,
            registration.Tags,
            HealthCheckRegistrationTimeout));
    }
}
