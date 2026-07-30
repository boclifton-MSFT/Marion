extern alias AppHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Azure.Messaging.ServiceBus;
using Marion.ApiService.Features.System;
using Marion.ApiService.Infrastructure.Messaging;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

public sealed class ServiceBusIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task API_publishes_a_versioned_traceable_synthetic_message_through_the_emulator()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>(
                ["--IntegrationTesting=true"],
                timeout.Token);

        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("messaging", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);

        var connectionString = await app.GetConnectionStringAsync("messaging", timeout.Token);
        await using var client = new ServiceBusClient(
            connectionString,
            new ServiceBusClientOptions
            {
                RetryOptions =
                {
                    MaxRetries = 0,
                    TryTimeout = TimeSpan.FromSeconds(15)
                }
            });
        await using var receiver = client.CreateReceiver(
            MessagingEntityNames.DocumentProcessingQueue);
        using var apiClient = app.CreateHttpClient("apiservice", "http");
        apiClient.Timeout = TimeSpan.FromSeconds(15);

        using var response = await apiClient.PostAsync(
            "/api/system/messaging/publish-synthetic",
            content: null,
            timeout.Token);
        var published = await response.Content.ReadFromJsonAsync<SyntheticPublishResponse>(
            timeout.Token);
        var received = await receiver.ReceiveMessageAsync(
            TimeSpan.FromSeconds(15),
            timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(published);
        Assert.NotNull(received);
        Assert.Equal(PlatformIntegrationRequested.EventTypeName, published.EventType);
        Assert.Equal(PlatformIntegrationRequested.CurrentVersion, published.Version);
        Assert.Equal(published.MessageId, received.MessageId);
        Assert.Equal(published.CorrelationId, received.CorrelationId);
        Assert.Equal(published.EventType, received.Subject);
        Assert.Equal("application/json", received.ContentType);
        Assert.Equal(published.EventType, received.ApplicationProperties["eventType"]);
        Assert.Equal(published.Version, received.ApplicationProperties["eventVersion"]);

        var envelope = received.Body.ToObjectFromJson<PlatformIntegrationRequested>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        Assert.Equal(published.MessageId, envelope.MessageId);
        Assert.Equal(published.CorrelationId, envelope.CorrelationId);
        Assert.Equal(published.EventType, envelope.EventType);
        Assert.Equal(published.Version, envelope.Version);
        Assert.Equal(published.OccurredAtUtc, envelope.OccurredAtUtc);

        await receiver.CompleteMessageAsync(received, timeout.Token);
    }
}
