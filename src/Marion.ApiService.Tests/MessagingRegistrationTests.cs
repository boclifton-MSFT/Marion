using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Marion.ApiService.Infrastructure.Configuration;
using Marion.ApiService.Infrastructure.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class MessagingRegistrationTests
{
    [Fact]
    public void Development_registers_the_Service_Bus_client_publisher_and_readiness_check()
    {
        using var factory = new MarionApiFactory("Development");
        using var scope = factory.Services.CreateScope();

        var registrations = scope.ServiceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        var messagingRegistration = Assert.Single(
            registrations,
            registration => registration.Name
                == MessagingServiceCollectionExtensions.ServiceBusHealthCheckName);

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServiceBusClient>());
        var sender = scope.ServiceProvider.GetRequiredService<ServiceBusSender>();
        Assert.Same(sender, scope.ServiceProvider.GetRequiredService<ServiceBusSender>());
        Assert.Equal(MessagingEntityNames.DocumentProcessingQueue, sender.EntityPath);
        Assert.IsType<AzureServiceBusPlatformIntegrationPublisher>(
            scope.ServiceProvider.GetRequiredService<IPlatformIntegrationPublisher>());
        Assert.DoesNotContain("live", messagingRegistration.Tags);
        Assert.Equal(HealthStatus.Unhealthy, messagingRegistration.FailureStatus);
        Assert.Equal(
            MessagingServiceCollectionExtensions.ConnectivityTimeout,
            messagingRegistration.Timeout);
    }

    [Fact]
    public void Testing_keeps_fast_API_tests_independent_from_the_emulator()
    {
        using var factory = new MarionApiFactory();
        using var scope = factory.Services.CreateScope();

        var registrations = scope.ServiceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.DoesNotContain(
            registrations,
            registration => registration.Name == "Azure_ServiceBusClient");
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServiceBusClient>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IPlatformIntegrationPublisher>());
    }

    [Fact]
    public void Local_mode_uses_the_named_emulator_connection()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                [$"{PlatformOptions.SectionName}:Mode"] = "Local",
                [$"{PlatformOptions.SectionName}:Local:BlobServiceUri"] =
                    "https://storage.local",
                [$"{PlatformOptions.SectionName}:Local:BlobContainerName"] = "documents",
                [$"{PlatformOptions.SectionName}:Local:ServiceBusFullyQualifiedNamespace"] =
                    "sbemulatorns",
                [$"{PlatformOptions.SectionName}:Local:SqlConnectionName"] = "mariondb",
                ["ConnectionStrings:messaging"] =
                    "Endpoint=sb://emulator.local:5672/;"
                    + "SharedAccessKeyName=RootManageSharedAccessKey;"
                    + "SharedAccessKey=SAS_KEY_VALUE;"
                    + "UseDevelopmentEmulator=true"
            });

        var client = host.Services.GetRequiredService<ServiceBusClient>();

        Assert.Equal("emulator.local", client.FullyQualifiedNamespace);
        Assert.Empty(host.Services.GetServices<TokenCredential>());
    }

    [Fact]
    public void Azure_mode_uses_the_explicit_namespace_and_shared_token_credential()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                [$"{PlatformOptions.SectionName}:Mode"] = "Azure",
                [$"{PlatformOptions.SectionName}:Azure:BlobServiceUri"] =
                    "https://documents.blob.core.windows.net",
                [$"{PlatformOptions.SectionName}:Azure:BlobContainerName"] = "documents",
                [$"{PlatformOptions.SectionName}:Azure:ServiceBusFullyQualifiedNamespace"] =
                    "explicit.servicebus.windows.net",
                [$"{PlatformOptions.SectionName}:Azure:SqlServer"] =
                    "marion.database.windows.net",
                [$"{PlatformOptions.SectionName}:Azure:SqlDatabase"] = "marion",
                [$"{PlatformOptions.SectionName}:Azure:Identity:TenantId"] = "tenant-id",
                ["ConnectionStrings:messaging"] =
                    "Endpoint=sb://sas-fallback.servicebus.windows.net/;"
                    + "SharedAccessKeyName=not-used;"
                    + "SharedAccessKey=not-used"
            });

        var tokenCredential = host.Services.GetRequiredService<TokenCredential>();
        var client = host.Services.GetRequiredService<ServiceBusClient>();

        Assert.Same(
            host.Services.GetRequiredService<DefaultAzureCredential>(),
            tokenCredential);
        Assert.Equal("explicit.servicebus.windows.net", client.FullyQualifiedNamespace);
    }

    [Fact]
    public async Task Connectivity_failure_is_bounded_and_does_not_expose_endpoint_details()
    {
        await using var client = new ServiceBusClient(
            "Endpoint=sb://127.0.0.1:1/;"
            + "SharedAccessKeyName=RootManageSharedAccessKey;"
            + "SharedAccessKey=SAS_KEY_VALUE;"
            + "UseDevelopmentEmulator=true",
            new ServiceBusClientOptions
            {
                RetryOptions =
                {
                    MaxRetries = 0,
                    TryTimeout = TimeSpan.FromMilliseconds(100)
                }
            });
        await using var sender = client.CreateSender(
            MessagingEntityNames.DocumentProcessingQueue);
        var healthCheck = new ServiceBusConnectivityHealthCheck(sender);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            timeout.Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("127.0.0.1", result.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("SAS_KEY_VALUE", result.Description, StringComparison.Ordinal);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Synthetic_envelopes_are_versioned_traceable_and_UTC()
    {
        var timestamp = new DateTimeOffset(2026, 7, 30, 16, 0, 0, TimeSpan.FromHours(-5));

        var envelope = PlatformIntegrationRequested.CreateSynthetic(timestamp);

        Assert.Equal(PlatformIntegrationRequested.EventTypeName, envelope.EventType);
        Assert.Equal(PlatformIntegrationRequested.CurrentVersion, envelope.Version);
        Assert.Equal(32, envelope.MessageId.Length);
        Assert.Equal(32, envelope.CorrelationId.Length);
        Assert.NotEqual(envelope.MessageId, envelope.CorrelationId);
        Assert.Equal(timestamp.ToUniversalTime(), envelope.OccurredAtUtc);
    }

    private static WebApplication BuildHost(IReadOnlyDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddPlatformConfiguration();
        builder.Services.AddPlatformIntegrationPublisher();
        return builder.Build();
    }
}
