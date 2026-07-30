namespace Marion.ApiService.Infrastructure.Storage;

public interface IDocumentStorageVerifier
{
    Task<DocumentStorageVerificationResult> VerifyAsync(
        CancellationToken cancellationToken);
}

public sealed record DocumentStorageVerificationResult(long DurationMilliseconds);
