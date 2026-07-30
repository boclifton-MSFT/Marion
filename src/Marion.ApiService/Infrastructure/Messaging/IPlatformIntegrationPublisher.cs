namespace Marion.ApiService.Infrastructure.Messaging;

internal interface IPlatformIntegrationPublisher
{
    Task<PlatformIntegrationRequested> PublishSyntheticAsync(CancellationToken cancellationToken);
}
