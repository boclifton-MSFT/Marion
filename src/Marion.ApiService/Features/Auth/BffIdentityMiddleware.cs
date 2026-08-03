using System.Security.Claims;

namespace Marion.ApiService.Features.Auth;

/// <summary>
/// Establishes the caller identity forwarded by the Nuxt BFF. The identity header is only
/// honoured when the shared key is also present, so a direct caller cannot assert a user.
/// </summary>
internal sealed class BffIdentityMiddleware(RequestDelegate next)
{
  internal const string UserIdHeaderName = "X-Marion-User-Id";

  public async Task InvokeAsync(HttpContext context, BffKeyValidator validator)
  {
    if (validator.Matches(context.Request)
        && context.Request.Headers.TryGetValue(UserIdHeaderName, out var values))
    {
      var userId = values.ToString();
      if (!string.IsNullOrWhiteSpace(userId))
      {
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            "MarionBff"));
      }
    }
    else
    {
      context.Request.Headers.Remove(UserIdHeaderName);
    }

    await next(context);
  }
}

internal static class BffIdentityMiddlewareExtensions
{
  internal static IApplicationBuilder UseBffIdentity(this IApplicationBuilder app)
  {
    return app.UseMiddleware<BffIdentityMiddleware>();
  }
}
