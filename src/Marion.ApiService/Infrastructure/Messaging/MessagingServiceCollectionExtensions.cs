using Azure.Core;
using Azure.Messaging.ServiceBus;
using Marion.ApiService.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Marion.ApiService.Infrastructure.Messaging;

internal static class MessagingServiceCollectionExtensions
{
    internal const string ServiceBusHealthCheckName = "Azure_ServiceBusClient";
    internal static readonly TimeSpan ConnectivityTimeout = TimeSpan.FromSeconds(5);

    private const string LocalServiceBusConnectionName = "messaging";

    internal static IServiceCollection AddPlatformIntegrationPublisher(
        this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ServiceBusClient>(CreateServiceBusClient);
        services.TryAddSingleton<IPlatformIntegrationPublisher,
            AzureServiceBusPlatformIntegrationPublisher>();
        services.PostConfigure<HealthCheckServiceOptions>(ConfigureHealthCheck);

        return services;
    }

    private static ServiceBusClient CreateServiceBusClient(IServiceProvider serviceProvider)
    {
        var platform = serviceProvider
            .GetRequiredService<IOptions<PlatformOptions>>()
            .Value;

        return platform.Mode switch
        {
            PlatformMode.Local => CreateLocalClient(
                serviceProvider.GetRequiredService<IConfiguration>()),
            PlatformMode.Azure => CreateAzureClient(
                platform.Azure,
                serviceProvider.GetRequiredService<TokenCredential>()),
            _ => throw new InvalidOperationException(
                "A supported Marion platform mode is required before registering Service Bus.")
        };
    }

    private static ServiceBusClient CreateLocalClient(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(LocalServiceBusConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The named local Service Bus emulator connection is required.");
        }

        return new ServiceBusClient(connectionString, CreateClientOptions());
    }

    private static ServiceBusClient CreateAzureClient(
        AzurePlatformOptions options,
        TokenCredential credential)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceBusFullyQualifiedNamespace))
        {
            throw new InvalidOperationException(
                "The Azure Service Bus fully qualified namespace is required.");
        }

        return new ServiceBusClient(
            options.ServiceBusFullyQualifiedNamespace,
            credential,
            CreateClientOptions());
    }

    private static ServiceBusClientOptions CreateClientOptions() =>
        new()
        {
            RetryOptions =
            {
                TryTimeout = ConnectivityTimeout
            }
        };

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
                new ServiceBusConnectivityHealthCheck(
                    serviceProvider.GetRequiredService<ServiceBusClient>())),
            HealthStatus.Unhealthy,
            registration.Tags,
            ConnectivityTimeout));
    }
}
