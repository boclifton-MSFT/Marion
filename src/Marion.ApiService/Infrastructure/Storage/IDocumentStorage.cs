namespace Marion.ApiService.Infrastructure.Storage;

public interface IDocumentStorage
{
    Task UploadAsync(
        string blobName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task<byte[]> DownloadAsync(
        string blobName,
        CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(
        string blobName,
        CancellationToken cancellationToken);

    Task CheckReadinessAsync(CancellationToken cancellationToken);
}
