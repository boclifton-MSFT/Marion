using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Marion.ApiService.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class ServiceBusConnectivityHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_uses_only_sender_batch_creation_without_sending_or_receiving()
    {
        var sender = new RecordingServiceBusSender();
        var healthCheck = new ServiceBusConnectivityHealthCheck(sender);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, sender.BatchCreationCount);
        Assert.Equal(0, sender.SendCount);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task CheckHealth_sanitizes_authentication_failures()
    {
        const string sensitiveDiagnostics = "tenant secret credential endpoint";
        var healthCheck = new ServiceBusConnectivityHealthCheck(
            new RecordingServiceBusSender(
                new AuthenticationFailedException(sensitiveDiagnostics)));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        AssertSanitizedFailure(result, sensitiveDiagnostics);
    }

    [Fact]
    public async Task CheckHealth_sanitizes_authorization_failures()
    {
        const string sensitiveDiagnostics = "tenant credential endpoint authorization";
        var healthCheck = new ServiceBusConnectivityHealthCheck(
            new RecordingServiceBusSender(
                new UnauthorizedAccessException(sensitiveDiagnostics)));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        AssertSanitizedFailure(result, sensitiveDiagnostics);
    }

    [Fact]
    public async Task CheckHealth_sanitizes_service_bus_failures_with_amqp_details()
    {
        const string sensitiveDiagnostics = "tenant credential endpoint amqp details";
        var healthCheck = new ServiceBusConnectivityHealthCheck(
            new RecordingServiceBusSender(
                new ServiceBusException(
                    sensitiveDiagnostics,
                    ServiceBusFailureReason.GeneralError,
                    "document-processing",
                    null)));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        AssertSanitizedFailure(result, sensitiveDiagnostics);
    }

    [Fact]
    public async Task CheckHealth_returns_a_sanitized_timeout_for_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var healthCheck = new ServiceBusConnectivityHealthCheck(
            new RecordingServiceBusSender(
                new OperationCanceledException(cancellation.Token)));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            cancellation.Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            "Service Bus connectivity check timed out.",
            result.Description);
        Assert.Null(result.Exception);
    }

    [Theory]
    [MemberData(nameof(NonAuthenticationFailures))]
    public async Task CheckHealth_sanitizes_transport_and_timeout_failures(Exception failure)
    {
        var healthCheck = new ServiceBusConnectivityHealthCheck(
            new RecordingServiceBusSender(failure));

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.DoesNotContain("sensitive", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> NonAuthenticationFailures() =>
    [
        [new HttpRequestException("sensitive HTTP endpoint details")],
        [new TimeoutException("sensitive timeout endpoint details")],
        [new InvalidOperationException("sensitive unexpected details")]
    ];

    private static void AssertSanitizedFailure(
        HealthCheckResult result,
        string sensitiveDiagnostics)
    {
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Service Bus connectivity check failed.", result.Description);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(
            sensitiveDiagnostics,
            result.Description,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingServiceBusSender : ServiceBusSender
    {
        private readonly Exception? failure;

        public RecordingServiceBusSender(Exception? failure = null)
        {
            this.failure = failure;
        }

        public int BatchCreationCount { get; private set; }

        public int SendCount { get; private set; }

        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(
            CancellationToken cancellationToken = default)
        {
            BatchCreationCount++;
            return failure is null
                ? ValueTask.FromResult<ServiceBusMessageBatch>(null!)
                : ValueTask.FromException<ServiceBusMessageBatch>(failure);
        }

        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        public override Task SendMessagesAsync(
            IEnumerable<ServiceBusMessage> messages,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        public override Task SendMessagesAsync(
            ServiceBusMessageBatch messageBatch,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }
}
