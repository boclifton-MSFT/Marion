using System.Diagnostics;
using System.Security.Cryptography;

namespace Marion.ApiService.Infrastructure.Storage;

internal sealed class DocumentStorageVerifier(
    IDocumentStorage storage,
    ILogger<DocumentStorageVerifier> logger)
    : IDocumentStorageVerifier
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task<DocumentStorageVerificationResult> VerifyAsync(
        CancellationToken cancellationToken)
    {
        var blobName = $"synthetic/{Guid.NewGuid():N}.probe";
        var expectedContent = RandomNumberGenerator.GetBytes(32);
        var stopwatch = Stopwatch.StartNew();
        var cleanupEligible = false;
        var verified = false;
        var cleanupCompleted = false;
        Exception? verificationException = null;
        using var operationTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationTimeout.CancelAfter(OperationTimeout);

        try
        {
            cleanupEligible = true;
            await storage.UploadAsync(
                blobName,
                expectedContent,
                operationTimeout.Token);

            var actualContent = await storage.DownloadAsync(
                blobName,
                operationTimeout.Token);
            if (!CryptographicOperations.FixedTimeEquals(expectedContent, actualContent))
            {
                throw new InvalidDataException("Synthetic document storage verification failed.");
            }

            verified = true;
        }
        catch (Exception exception)
        {
            verificationException = exception;
            throw;
        }
        finally
        {
            try
            {
                if (cleanupEligible)
                {
                    using var cleanupTimeout = new CancellationTokenSource(CleanupTimeout);
                    await storage.DeleteIfExistsAsync(blobName, cleanupTimeout.Token);
                }

                cleanupCompleted = true;
            }
            catch (Exception) when (verificationException is not null)
            {
                logger.LogWarning(
                    "Document storage synthetic verification cleanup failed after verification failed.");
            }
            finally
            {
                stopwatch.Stop();
                logger.LogInformation(
                    "Document storage synthetic verification {Outcome} in {DurationMilliseconds} ms.",
                    verified && cleanupCompleted ? "succeeded" : "failed",
                    stopwatch.ElapsedMilliseconds);
            }
        }

        return new DocumentStorageVerificationResult(stopwatch.ElapsedMilliseconds);
    }
}
