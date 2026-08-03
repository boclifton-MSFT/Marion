using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Marion.ApiService.Features.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class AuthEndpointsTests
{
  private static readonly object ValidTransaction = new
  {
    transactionId = "transaction-1",
    expiresAt = 1_700_000_300_000L
  };

  [Fact]
  public async Task Internal_auth_surface_is_unreachable_without_the_shared_key()
  {
    using var factory = new MarionApiFactory();
    using var client = factory.CreateClient();

    using var missingKey = await client.PostAsJsonAsync(
        "/internal/auth/transactions",
        ValidTransaction,
        CancellationToken.None);

    client.DefaultRequestHeaders.Add(BffKeyValidator.HeaderName, "not-the-key");
    using var wrongKey = await client.PostAsJsonAsync(
        "/internal/auth/transactions",
        ValidTransaction,
        CancellationToken.None);

    Assert.Equal(HttpStatusCode.Unauthorized, missingKey.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
  }

  [Fact]
  public async Task Internal_auth_surface_is_not_reachable_through_the_public_api_prefix()
  {
    using var factory = new MarionApiFactory();
    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(BffKeyValidator.HeaderName, MarionApiFactory.BffKey);

    using var response = await client.PostAsJsonAsync(
        "/api/internal/auth/transactions",
        ValidTransaction,
        CancellationToken.None);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Theory]
  [InlineData("/internal/auth/transactions", """{"transactionId":"","expiresAt":1700000300000}""")]
  [InlineData("/internal/auth/transactions", """{"transactionId":"tx","expiresAt":-1}""")]
  [InlineData("/internal/auth/transactions/consume", """{"transactionId":null,"now":1700000000000}""")]
  [InlineData("/internal/auth/identities/resolve", """{"issuer":"","subject":"s","now":1700000000000}""")]
  [InlineData("/internal/auth/sessions", """{"sessionId":"s","userId":"","issuedAt":1,"lastActiveAt":1}""")]
  [InlineData("/internal/auth/sessions/touch", """{"session":null,"now":1700000000000}""")]
  [InlineData("/internal/auth/sessions/rotate", """{"previousSessionId":"a","session":null}""")]
  public async Task Malformed_auth_payloads_are_rejected_before_reaching_the_store(
      string path,
      string payload)
  {
    using var factory = new MarionApiFactory();
    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add(BffKeyValidator.HeaderName, MarionApiFactory.BffKey);

    using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
    using var response = await client.PostAsync(
        path,
        content,
        CancellationToken.None);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Forwarded_user_identity_is_honoured_only_alongside_the_shared_key()
  {
    var validator = new BffKeyValidator(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["Auth:BffKey"] = MarionApiFactory.BffKey
        })
        .Build());
    var middleware = new BffIdentityMiddleware(_ => Task.CompletedTask);

    var spoofed = new DefaultHttpContext();
    spoofed.Request.Headers[BffIdentityMiddleware.UserIdHeaderName] = "spoofed-user";
    await middleware.InvokeAsync(spoofed, validator);

    var trusted = new DefaultHttpContext();
    trusted.Request.Headers[BffKeyValidator.HeaderName] = MarionApiFactory.BffKey;
    trusted.Request.Headers[BffIdentityMiddleware.UserIdHeaderName] = "user-1";
    await middleware.InvokeAsync(trusted, validator);

    Assert.False(spoofed.User.Identity?.IsAuthenticated);
    Assert.False(spoofed.Request.Headers.ContainsKey(BffIdentityMiddleware.UserIdHeaderName));
    Assert.True(trusted.User.Identity?.IsAuthenticated);
    Assert.Equal(
        "user-1",
        trusted.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
  }
}
