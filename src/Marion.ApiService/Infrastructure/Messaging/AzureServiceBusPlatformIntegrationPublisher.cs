using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Marion.ApiService.Infrastructure.Messaging;

internal sealed class AzureServiceBusPlatformIntegrationPublisher(
    ServiceBusClient client,
    TimeProvider timeProvider)
    : IPlatformIntegrationPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PlatformIntegrationRequested> PublishSyntheticAsync(
        CancellationToken cancellationToken)
    {
        var envelope = PlatformIntegrationRequested.CreateSynthetic(timeProvider.GetUtcNow());
        var message = new ServiceBusMessage(
            BinaryData.FromObjectAsJson(envelope, SerializerOptions))
        {
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            Subject = envelope.EventType,
            ContentType = "application/json"
        };
        message.ApplicationProperties["eventType"] = envelope.EventType;
        message.ApplicationProperties["eventVersion"] = envelope.Version;
        message.ApplicationProperties["occurredAtUtc"] =
            envelope.OccurredAtUtc.ToString("O");

        await using var sender = client.CreateSender(
            MessagingEntityNames.DocumentProcessingQueue);
        await sender.SendMessageAsync(message, cancellationToken);

        return envelope;
    }
}
