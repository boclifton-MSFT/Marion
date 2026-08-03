using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Marion.ApiService.Infrastructure.Persistence;

internal sealed class AuthRepository(MarionDbContext dbContext) : IAuthRepository
{
  private const int DuplicateKeyErrorNumber = 2601;
  private const int UniqueConstraintErrorNumber = 2627;

  public async Task CreateTransactionAsync(
      string transactionId,
      DateTime expiresAt,
      DateTime now,
      CancellationToken cancellationToken)
  {
    await dbContext.AuthTransactions
        .Where(transaction => transaction.ExpiresAt < now)
        .ExecuteDeleteAsync(cancellationToken);

    dbContext.ChangeTracker.Clear();
    dbContext.AuthTransactions.Add(new AuthTransaction
    {
      TransactionId = transactionId,
      ExpiresAt = expiresAt
    });

    await dbContext.SaveChangesAsync(cancellationToken);
  }

  public async Task<bool> ConsumeTransactionAsync(
      string transactionId,
      DateTime now,
      CancellationToken cancellationToken)
  {
    // A single conditional delete is what makes a transaction usable exactly once.
    var consumed = await dbContext.AuthTransactions
        .Where(transaction => transaction.TransactionId == transactionId
            && transaction.ExpiresAt >= now)
        .ExecuteDeleteAsync(cancellationToken);

    return consumed == 1;
  }

  public async Task<string> ResolveIdentityAsync(
      string issuer,
      string subject,
      DateTime now,
      CancellationToken cancellationToken)
  {
    var existing = await FindUserIdAsync(issuer, subject, cancellationToken);
    if (existing is not null)
    {
      return existing;
    }

    var userId = Guid.CreateVersion7().ToString();
    try
    {
      dbContext.ChangeTracker.Clear();
      dbContext.ExternalIdentities.Add(new ExternalIdentity
      {
        Issuer = issuer,
        Subject = subject,
        UserId = userId,
        CreatedAt = now
      });

      await dbContext.SaveChangesAsync(cancellationToken);
      return userId;
    }
    catch (DbUpdateException exception) when (IsUniqueViolation(exception))
    {
      // Concurrent first sign-in for the same subject; adopt the winner's user.
      dbContext.ChangeTracker.Clear();
      return await FindUserIdAsync(issuer, subject, cancellationToken)
          ?? throw new InvalidOperationException(
              "The external identity conflicted but could not be resolved.");
    }
  }

  public async Task CreateSessionAsync(AuthSession session, CancellationToken cancellationToken)
  {
    dbContext.ChangeTracker.Clear();
    dbContext.AuthSessions.Add(session);
    await dbContext.SaveChangesAsync(cancellationToken);
  }

  public async Task<AuthSession?> GetSessionAsync(
      string sessionId,
      CancellationToken cancellationToken)
  {
    return await dbContext.AuthSessions
        .AsNoTracking()
        .FirstOrDefaultAsync(session => session.SessionId == sessionId, cancellationToken);
  }

  public async Task<AuthSession?> TouchSessionAsync(
      AuthSession expected,
      DateTime now,
      CancellationToken cancellationToken)
  {
    // Matching every prior column makes this a compare-and-swap: a stale caller updates nothing.
    var touched = await dbContext.AuthSessions
        .Where(session => session.SessionId == expected.SessionId
            && session.UserId == expected.UserId
            && session.IssuedAt == expected.IssuedAt
            && session.LastActiveAt == expected.LastActiveAt)
        .ExecuteUpdateAsync(
            setters => setters.SetProperty(session => session.LastActiveAt, now),
            cancellationToken);

    return touched == 1
        ? new AuthSession
        {
          SessionId = expected.SessionId,
          UserId = expected.UserId,
          IssuedAt = expected.IssuedAt,
          LastActiveAt = now
        }
        : null;
  }

  public async Task RotateSessionAsync(
      string? previousSessionId,
      AuthSession session,
      CancellationToken cancellationToken)
  {
    var strategy = dbContext.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async () =>
    {
      dbContext.ChangeTracker.Clear();
      await using var transaction = await dbContext.Database
              .BeginTransactionAsync(cancellationToken);

      if (!string.IsNullOrEmpty(previousSessionId))
      {
        await dbContext.AuthSessions
                .Where(existing => existing.SessionId == previousSessionId)
                .ExecuteDeleteAsync(cancellationToken);
      }

      dbContext.AuthSessions.Add(session);
      await dbContext.SaveChangesAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    });
  }

  public async Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken)
  {
    await dbContext.AuthSessions
        .Where(session => session.SessionId == sessionId)
        .ExecuteDeleteAsync(cancellationToken);
  }

  private Task<string?> FindUserIdAsync(
      string issuer,
      string subject,
      CancellationToken cancellationToken)
  {
    return dbContext.ExternalIdentities
        .AsNoTracking()
        .Where(identity => identity.Issuer == issuer && identity.Subject == subject)
        .Select(identity => identity.UserId)
        .FirstOrDefaultAsync(cancellationToken);
  }

  private static bool IsUniqueViolation(DbUpdateException exception)
  {
    return exception.InnerException is SqlException sqlException
        && sqlException.Number is DuplicateKeyErrorNumber or UniqueConstraintErrorNumber;
  }
}
