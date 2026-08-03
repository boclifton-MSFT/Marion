using System.Security.Cryptography;
using System.Text;

namespace Marion.ApiService.Features.Auth;

/// <summary>
/// Shared-secret gate for the internal auth surface. The Nuxt BFF is the only intended caller.
/// </summary>
internal sealed class BffKeyValidator
{
  internal const string HeaderName = "X-Marion-Bff-Key";

  private readonly byte[]? key;

  public BffKeyValidator(IConfiguration configuration)
  {
    var configured = configuration["Auth:BffKey"];
    key = string.IsNullOrWhiteSpace(configured) ? null : Encoding.UTF8.GetBytes(configured);
  }

  internal bool IsConfigured => key is not null;

  internal bool Matches(HttpRequest request)
  {
    if (key is null || !request.Headers.TryGetValue(HeaderName, out var values))
    {
      return false;
    }

    var presented = values.ToString();
    if (string.IsNullOrEmpty(presented))
    {
      return false;
    }

    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), key);
  }
}

internal sealed class BffKeyEndpointFilter(BffKeyValidator validator) : IEndpointFilter
{
  public async ValueTask<object?> InvokeAsync(
      EndpointFilterInvocationContext context,
      EndpointFilterDelegate next)
  {
    if (!validator.Matches(context.HttpContext.Request))
    {
      return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    return await next(context);
  }
}
