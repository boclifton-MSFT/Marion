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

    [Fact]
    public async Task Verify_deletes_the_blob_when_upload_persists_then_throws()
    {
        var uploadException = new IOException("Synthetic upload failure.");
        var storage = new RecordingDocumentStorage
        {
            UploadException = uploadException
        };
        var verifier = CreateVerifier(storage);

        var observedException = await Assert.ThrowsAsync<IOException>(
            () => verifier.VerifyAsync(CancellationToken.None));

        Assert.Same(uploadException, observedException);
        Assert.False(storage.BlobExists);
        Assert.Equal(
            ["upload", "delete"],
            storage.Operations.Select(operation => operation.Operation));
    }

    [Fact]
    public async Task Verify_uses_independent_cleanup_when_upload_is_canceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var uploadException = new OperationCanceledException(cancellation.Token);
        var storage = new RecordingDocumentStorage
        {
            UploadException = uploadException
        };
        var verifier = CreateVerifier(storage);

        var observedException = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyAsync(cancellation.Token));

        Assert.Same(uploadException, observedException);
        Assert.False(storage.BlobExists);
        var deleteOperation = Assert.Single(
            storage.Operations,
            operation => operation.Operation == "delete");
        Assert.False(deleteOperation.CancellationRequested);
    }

    [Fact]
    public async Task Verify_preserves_upload_failure_when_cleanup_also_fails()
    {
        var uploadException = new IOException("Synthetic upload failure.");
        var storage = new RecordingDocumentStorage
        {
            UploadException = uploadException,
            DeleteException = new IOException("Synthetic cleanup failure.")
        };
        var verifier = CreateVerifier(storage);

        var observedException = await Assert.ThrowsAsync<IOException>(
            () => verifier.VerifyAsync(CancellationToken.None));

        Assert.Same(uploadException, observedException);
        Assert.True(storage.BlobExists);
        Assert.Equal(
            ["upload", "delete"],
            storage.Operations.Select(operation => operation.Operation));
    }

    private static DocumentStorageVerifier CreateVerifier(IDocumentStorage storage) =>
        new(storage, NullLogger<DocumentStorageVerifier>.Instance);

    private sealed class RecordingDocumentStorage : IDocumentStorage
    {
        private byte[]? uploadedContent;

        internal List<StorageOperation> Operations { get; } = [];

        internal bool BlobExists => uploadedContent is not null;

        internal byte[]? DownloadOverride { get; init; }

        internal Exception? DownloadException { get; init; }

        internal Exception? UploadException { get; init; }

        internal Exception? DeleteException { get; init; }

        public Task UploadAsync(
            string blobName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            Operations.Add(new StorageOperation(
                "upload",
                blobName,
                cancellationToken.IsCancellationRequested));
            uploadedContent = content.ToArray();

            if (UploadException is not null)
            {
                return Task.FromException(UploadException);
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> DownloadAsync(
            string blobName,
            CancellationToken cancellationToken)
        {
            Operations.Add(new StorageOperation(
                "read",
                blobName,
                cancellationToken.IsCancellationRequested));

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
            Operations.Add(new StorageOperation(
                "delete",
                blobName,
                cancellationToken.IsCancellationRequested));

            if (DeleteException is not null)
            {
                return Task.FromException(DeleteException);
            }

            uploadedContent = null;
            return Task.CompletedTask;
        }

        public Task CheckReadinessAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record StorageOperation(
        string Operation,
        string BlobName,
        bool CancellationRequested);
}
