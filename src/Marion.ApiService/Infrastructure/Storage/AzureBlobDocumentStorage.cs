using Azure.Storage.Blobs;

namespace Marion.ApiService.Infrastructure.Storage;

internal sealed class AzureBlobDocumentStorage(BlobContainerClient containerClient)
    : IDocumentStorage
{
    public async Task UploadAsync(
        string blobName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            BinaryData.FromBytes(content),
            overwrite: false,
            cancellationToken);
    }

    public async Task<byte[]> DownloadAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var response = await containerClient
            .GetBlobClient(blobName)
            .DownloadContentAsync(cancellationToken);

        return response.Value.Content.ToArray();
    }

    public async Task DeleteIfExistsAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        await containerClient.DeleteBlobIfExistsAsync(
            blobName,
            cancellationToken: cancellationToken);
    }

    public async Task CheckReadinessAsync(CancellationToken cancellationToken)
    {
        await containerClient.GetPropertiesAsync(cancellationToken: cancellationToken);
    }
}
