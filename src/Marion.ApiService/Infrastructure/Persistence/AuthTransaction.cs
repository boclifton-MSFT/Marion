namespace Marion.ApiService.Infrastructure.Persistence;

/// <summary>Single-use OAuth transaction record that prevents authorization code replay.</summary>
public sealed class AuthTransaction
{
  public required string TransactionId { get; init; }

  public required DateTime ExpiresAt { get; init; }
}
