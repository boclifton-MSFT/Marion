namespace Marion.ApiService.Infrastructure.Persistence;

/// <summary>Server-side session record that makes browser sessions revocable.</summary>
public sealed class AuthSession
{
  public required string SessionId { get; init; }

  public required string UserId { get; init; }

  public required DateTime IssuedAt { get; init; }

  public required DateTime LastActiveAt { get; set; }
}
