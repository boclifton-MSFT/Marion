using Azure.Messaging.ServiceBus;
using Marion.ApiService.Infrastructure.Messaging;
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
            registration => registration.Name == "Azure_ServiceBusClient");

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServiceBusClient>());
        Assert.IsType<AzureServiceBusPlatformIntegrationPublisher>(
            scope.ServiceProvider.GetRequiredService<IPlatformIntegrationPublisher>());
        Assert.DoesNotContain("live", messagingRegistration.Tags);
        Assert.Equal(HealthStatus.Unhealthy, messagingRegistration.FailureStatus);
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
}
