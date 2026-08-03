using Marion.ApiService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;

namespace Marion.ApiService.Features.Auth;

internal static class AuthEndpoints
{
  /// <summary>
  /// Mapped outside <c>/api</c> so the frontend's catch-all API proxy cannot reach these routes.
  /// </summary>
  internal static void MapInternalAuthEndpoints(this IEndpointRouteBuilder endpoints)
  {
    var auth = endpoints.MapGroup("/internal/auth")
        .AddEndpointFilter<BffKeyEndpointFilter>()
        .ExcludeFromDescription();

    auth.MapPost("/transactions", async (
        CreateTransactionRequest request,
        IAuthRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
      if (string.IsNullOrWhiteSpace(request.TransactionId)
              || !AuthContracts.TryReadTimestamp(request.ExpiresAt, out var expiresAt))
      {
        return Results.BadRequest();
      }

      await repository.CreateTransactionAsync(
              request.TransactionId,
              expiresAt,
              timeProvider.GetUtcNow().UtcDateTime,
              cancellationToken);

      return Results.NoContent();
    });

    auth.MapPost("/transactions/consume", async (
        ConsumeTransactionRequest request,
        IAuthRepository repository,
        CancellationToken cancellationToken) =>
    {
      if (string.IsNullOrWhiteSpace(request.TransactionId)
              || !AuthContracts.TryReadTimestamp(request.Now, out var now))
      {
        return Results.BadRequest();
      }

      var consumed = await repository.ConsumeTransactionAsync(
              request.TransactionId,
              now,
              cancellationToken);

      return Results.Ok(new ConsumeTransactionResponse(consumed));
    });

    auth.MapPost("/identities/resolve", async (
        ResolveIdentityRequest request,
        IAuthRepository repository,
        CancellationToken cancellationToken) =>
    {
      if (string.IsNullOrWhiteSpace(request.Issuer)
              || string.IsNullOrWhiteSpace(request.Subject)
              || !AuthContracts.TryReadTimestamp(request.Now, out var now))
      {
        return Results.BadRequest();
      }

      var userId = await repository.ResolveIdentityAsync(
              request.Issuer,
              request.Subject,
              now,
              cancellationToken);

      return Results.Ok(new ResolveIdentityResponse(userId));
    });

    auth.MapPost("/sessions", async (
        SessionPayload payload,
        IAuthRepository repository,
        CancellationToken cancellationToken) =>
    {
      if (!AuthContracts.TryReadSession(payload, out var session))
      {
        return Results.BadRequest();
      }

      await repository.CreateSessionAsync(session, cancellationToken);
      return Results.NoContent();
    });

    auth.MapGet("/sessions/{sessionId}", async (
        string sessionId,
        IAuthRepository repository,
        CancellationToken cancellationToken) =>
    {
      var session = await repository.GetSessionAsync(sessionId, cancellationToken);
      return session is null
              ? Results.NotFound()
              : Results.Ok(AuthContracts.ToPayload(session));
    });

    auth.MapPost("/sessions/touch", async (
        TouchSessionRequest request,
        IAuthRepository repository,
        CancellationToken cancellationToken) =>
    {
      if (!AuthContracts.TryReadSession(request.Session, out var expected)
              || !AuthContracts.TryReadTimestamp(request.Now, out var now))
      {
        return Results.BadRequest();
      }

      var touched = await repository.TouchSessionAsync(expected, now, cancellationToken);
      return touched is null
              ? Results.Conflict()
              : Results.Ok(AuthContracts.ToPayload(touched));
    });

    auth.MapPost("/sessions/rotate", async (
        RotateSessionRequest request,
        IAuthRepository repository,
        CancellationToken cancellationToken) =>
    {
      if (!AuthContracts.TryReadSession(request.Session, out var session))
      {
        return Results.BadRequest();
      }

      await repository.RotateSessionAsync(
              request.PreviousSessionId,
              session,
              cancellationToken);

      return Results.NoContent();
    });

    auth.MapDelete("/sessions/{sessionId}", async (
        string sessionId,
        IAuthRepository repository,
        CancellationToken cancellationToken) =>
    {
      await repository.RevokeSessionAsync(sessionId, cancellationToken);
      return Results.NoContent();
    });
  }
}
