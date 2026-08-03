extern alias AppHost;

using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Marion.ApiService.Features.Auth;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

[Collection(DistributedApplicationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuthStoreIntegrationTests
{
  private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);
  private const string BffKey = "integration-bff-key-sentinel";

  [Fact]
  public async Task Auth_store_enforces_single_use_transactions_revocation_and_stable_identities()
  {
    using var timeout = new CancellationTokenSource(TestTimeout);
    var builder = await DistributedApplicationTestingBuilder.CreateAsync<AppHostProjects.Marion_AppHost>(
        ["--IntegrationTesting=true", $"--Parameters:bff-key={BffKey}"],
        timeout.Token);

    await using var app = await builder.BuildAsync(timeout.Token);
    await app.StartAsync(timeout.Token);
    await app.ResourceNotifications.WaitForResourceHealthyAsync("sql", timeout.Token);
    await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);

    using var client = app.CreateHttpClient("apiservice", "http");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add(BffKeyValidator.HeaderName, BffKey);

    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // A transaction may be consumed exactly once, which is what defeats callback replay.
    using var createdTransaction = await client.PostAsJsonAsync(
        "/internal/auth/transactions",
        new { transactionId = "tx-1", expiresAt = now + 300_000 },
        timeout.Token);
    var firstConsume = await ConsumeAsync(client, "tx-1", now, timeout.Token);
    var secondConsume = await ConsumeAsync(client, "tx-1", now, timeout.Token);

    Assert.Equal(HttpStatusCode.NoContent, createdTransaction.StatusCode);
    Assert.True(firstConsume);
    Assert.False(secondConsume);

    // An expired transaction is never usable.
    using var expiredTransaction = await client.PostAsJsonAsync(
        "/internal/auth/transactions",
        new { transactionId = "tx-expired", expiresAt = now - 1 },
        timeout.Token);
    Assert.Equal(HttpStatusCode.NoContent, expiredTransaction.StatusCode);
    Assert.False(await ConsumeAsync(client, "tx-expired", now, timeout.Token));

    // The same external subject always maps to the same Marion user.
    var firstUserId = await ResolveAsync(client, "https://accounts.google.com", "subject-1", now, timeout.Token);
    var repeatUserId = await ResolveAsync(client, "https://accounts.google.com", "subject-1", now, timeout.Token);
    var otherUserId = await ResolveAsync(client, "https://accounts.google.com", "subject-2", now, timeout.Token);

    Assert.Equal(firstUserId, repeatUserId);
    Assert.NotEqual(firstUserId, otherUserId);

    // Sessions rotate, slide, and revoke.
    using var rotated = await client.PostAsJsonAsync(
        "/internal/auth/sessions/rotate",
        new
        {
          previousSessionId = (string?)null,
          session = new
          {
            sessionId = "session-1",
            userId = firstUserId,
            issuedAt = now,
            lastActiveAt = now
          }
        },
        timeout.Token);
    Assert.Equal(HttpStatusCode.NoContent, rotated.StatusCode);

    using var fetched = await client.GetAsync("/internal/auth/sessions/session-1", timeout.Token);
    var stored = await fetched.Content.ReadFromJsonAsync<SessionResponse>(timeout.Token);
    Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    Assert.NotNull(stored);
    Assert.Equal(firstUserId, stored.UserId);

    using var touched = await TouchAsync(client, stored, now + 1_000, timeout.Token);
    var slid = await touched.Content.ReadFromJsonAsync<SessionResponse>(timeout.Token);
    Assert.Equal(HttpStatusCode.OK, touched.StatusCode);
    Assert.NotNull(slid);
    Assert.Equal(now + 1_000, slid.LastActiveAt);

    // Replaying the pre-touch state must lose the compare-and-swap.
    using var staleTouch = await TouchAsync(client, stored, now + 2_000, timeout.Token);
    Assert.Equal(HttpStatusCode.Conflict, staleTouch.StatusCode);

    using var revoked = await client.DeleteAsync("/internal/auth/sessions/session-1", timeout.Token);
    using var afterRevoke = await client.GetAsync("/internal/auth/sessions/session-1", timeout.Token);
    Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, afterRevoke.StatusCode);
  }

  private static async Task<bool> ConsumeAsync(
      HttpClient client,
      string transactionId,
      long now,
      CancellationToken cancellationToken)
  {
    using var response = await client.PostAsJsonAsync(
        "/internal/auth/transactions/consume",
        new { transactionId, now },
        cancellationToken);
    var result = await response.Content.ReadFromJsonAsync<ConsumeResponse>(cancellationToken);
    return result?.Consumed ?? false;
  }

  private static async Task<string> ResolveAsync(
      HttpClient client,
      string issuer,
      string subject,
      long now,
      CancellationToken cancellationToken)
  {
    using var response = await client.PostAsJsonAsync(
        "/internal/auth/identities/resolve",
        new { issuer, subject, now },
        cancellationToken);
    var result = await response.Content.ReadFromJsonAsync<ResolveResponse>(cancellationToken);
    Assert.NotNull(result?.UserId);
    return result.UserId;
  }

  private static Task<HttpResponseMessage> TouchAsync(
      HttpClient client,
      SessionResponse session,
      long now,
      CancellationToken cancellationToken)
  {
    return client.PostAsJsonAsync(
        "/internal/auth/sessions/touch",
        new { session, now },
        cancellationToken);
  }

  private sealed record ConsumeResponse(bool Consumed);

  private sealed record ResolveResponse(string UserId);

  private sealed record SessionResponse(
      string SessionId,
      string UserId,
      long IssuedAt,
      long LastActiveAt);
}
