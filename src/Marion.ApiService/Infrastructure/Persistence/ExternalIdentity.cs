namespace Marion.ApiService.Infrastructure.Persistence;

/// <summary>Maps an immutable external identity provider subject to a Marion user.</summary>
public sealed class ExternalIdentity
{
  public required string Issuer { get; init; }

  public required string Subject { get; init; }

  public required string UserId { get; init; }

  public required DateTime CreatedAt { get; init; }
}
