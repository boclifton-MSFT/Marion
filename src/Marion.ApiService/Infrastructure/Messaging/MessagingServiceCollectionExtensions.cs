using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marion.ApiService.Infrastructure.Messaging;

internal static class MessagingServiceCollectionExtensions
{
    internal static IServiceCollection AddPlatformIntegrationPublisher(
        this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPlatformIntegrationPublisher,
            AzureServiceBusPlatformIntegrationPublisher>();

        return services;
    }
}
