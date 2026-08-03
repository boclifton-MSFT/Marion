namespace Marion.ApiService.Infrastructure.Persistence;

/// <summary>
/// Durable authentication state. Every operation here is atomic on the server so that the
/// browser-facing tier cannot weaken replay, revocation, or identity-provisioning guarantees.
/// </summary>
public interface IAuthRepository
{
    Task CreateTransactionAsync(
        string transactionId,
        DateTime expiresAt,
        DateTime now,
        CancellationToken cancellationToken);

    Task<bool> ConsumeTransactionAsync(
        string transactionId,
        DateTime now,
        CancellationToken cancellationToken);

    Task<string> ResolveIdentityAsync(
        string issuer,
        string subject,
        DateTime now,
        CancellationToken cancellationToken);

    Task CreateSessionAsync(AuthSession session, CancellationToken cancellationToken);

    Task<AuthSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task<AuthSession?> TouchSessionAsync(
        AuthSession expected,
        DateTime now,
        CancellationToken cancellationToken);

    Task RotateSessionAsync(
        string? previousSessionId,
        AuthSession session,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken);
}
