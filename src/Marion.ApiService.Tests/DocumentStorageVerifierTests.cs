using Marion.ApiService.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class DocumentStorageVerifierTests
{
    [Fact]
    public async Task Verify_uses_unique_names_and_upload_read_delete_order()
    {
        var storage = new RecordingDocumentStorage();
        var verifier = CreateVerifier(storage);

        await verifier.VerifyAsync(CancellationToken.None);
        await verifier.VerifyAsync(CancellationToken.None);

        Assert.Equal(
            ["upload", "read", "delete", "upload", "read", "delete"],
            storage.Operations.Select(operation => operation.Operation));
        Assert.All(
            storage.Operations.Where(operation => operation.Operation == "upload"),
            operation => Assert.StartsWith("synthetic/", operation.BlobName));
        Assert.Equal(2, storage.Operations
            .Where(operation => operation.Operation == "upload")
            .Select(operation => operation.BlobName)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public async Task Verify_deletes_the_blob_when_content_does_not_match()
    {
        var storage = new RecordingDocumentStorage
        {
            DownloadOverride = [0x00]
        };
        var verifier = CreateVerifier(storage);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => verifier.VerifyAsync(CancellationToken.None));

        Assert.Equal(
            ["upload", "read", "delete"],
            storage.Operations.Select(operation => operation.Operation));
    }

    [Fact]
    public async Task Verify_deletes_the_blob_when_reading_fails()
    {
        var storage = new RecordingDocumentStorage
        {
            DownloadException = new IOException("Synthetic read failure.")
        };
        var verifier = CreateVerifier(storage);

        await Assert.ThrowsAsync<IOException>(
            () => verifier.VerifyAsync(CancellationToken.None));

        Assert.Equal(
            ["upload", "read", "delete"],
            storage.Operations.Select(operation => operation.Operation));
    }

    private static DocumentStorageVerifier CreateVerifier(IDocumentStorage storage) =>
        new(storage, NullLogger<DocumentStorageVerifier>.Instance);

    private sealed class RecordingDocumentStorage : IDocumentStorage
    {
        private byte[]? uploadedContent;

        internal List<StorageOperation> Operations { get; } = [];

        internal byte[]? DownloadOverride { get; init; }

        internal Exception? DownloadException { get; init; }

        public Task UploadAsync(
            string blobName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            Operations.Add(new StorageOperation("upload", blobName));
            uploadedContent = content.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]> DownloadAsync(
            string blobName,
            CancellationToken cancellationToken)
        {
            Operations.Add(new StorageOperation("read", blobName));

            if (DownloadException is not null)
            {
                return Task.FromException<byte[]>(DownloadException);
            }

            return Task.FromResult(
                DownloadOverride ?? uploadedContent ?? throw new InvalidOperationException());
        }

        public Task DeleteIfExistsAsync(
            string blobName,
            CancellationToken cancellationToken)
        {
            Operations.Add(new StorageOperation("delete", blobName));
            return Task.CompletedTask;
        }

        public Task CheckReadinessAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record StorageOperation(string Operation, string BlobName);
}
