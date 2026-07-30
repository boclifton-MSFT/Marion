namespace Marion.ApiService.Infrastructure.Messaging;

internal sealed record PlatformIntegrationRequested(
    string MessageId,
    string CorrelationId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAtUtc)
{
    internal const string EventTypeName = "PlatformIntegrationRequested";
    internal const int CurrentVersion = 1;

    internal static PlatformIntegrationRequested CreateSynthetic(DateTimeOffset occurredAtUtc)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        return new PlatformIntegrationRequested(
            Guid.NewGuid().ToString("N"),
            correlationId,
            EventTypeName,
            CurrentVersion,
            occurredAtUtc.ToUniversalTime());
    }
}
