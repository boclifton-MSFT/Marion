using Marion.ApiService.Infrastructure.Persistence;

namespace Marion.ApiService.Features.Auth;

internal sealed record CreateTransactionRequest(string? TransactionId, long ExpiresAt);

internal sealed record ConsumeTransactionRequest(string? TransactionId, long Now);

internal sealed record ConsumeTransactionResponse(bool Consumed);

internal sealed record ResolveIdentityRequest(string? Issuer, string? Subject, long Now);

internal sealed record ResolveIdentityResponse(string UserId);

internal sealed record SessionPayload(
    string? SessionId,
    string? UserId,
    long IssuedAt,
    long LastActiveAt);

internal sealed record TouchSessionRequest(SessionPayload? Session, long Now);

internal sealed record RotateSessionRequest(string? PreviousSessionId, SessionPayload? Session);

internal static class AuthContracts
{
  private const long MaxEpochMilliseconds = 253402300799999;

  internal static bool TryReadTimestamp(long epochMilliseconds, out DateTime value)
  {
    if (epochMilliseconds is < 0 or > MaxEpochMilliseconds)
    {
      value = default;
      return false;
    }

    value = DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds).UtcDateTime;
    return true;
  }

  internal static long ToEpochMilliseconds(DateTime value)
  {
    return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
        .ToUnixTimeMilliseconds();
  }

  internal static bool TryReadSession(SessionPayload? payload, out AuthSession session)
  {
    session = default!;
    if (payload is null
        || string.IsNullOrWhiteSpace(payload.SessionId)
        || string.IsNullOrWhiteSpace(payload.UserId)
        || !TryReadTimestamp(payload.IssuedAt, out var issuedAt)
        || !TryReadTimestamp(payload.LastActiveAt, out var lastActiveAt))
    {
      return false;
    }

    session = new AuthSession
    {
      SessionId = payload.SessionId,
      UserId = payload.UserId,
      IssuedAt = issuedAt,
      LastActiveAt = lastActiveAt
    };
    return true;
  }

  internal static SessionPayload ToPayload(AuthSession session)
  {
    return new SessionPayload(
        session.SessionId,
        session.UserId,
        ToEpochMilliseconds(session.IssuedAt),
        ToEpochMilliseconds(session.LastActiveAt));
  }
}
