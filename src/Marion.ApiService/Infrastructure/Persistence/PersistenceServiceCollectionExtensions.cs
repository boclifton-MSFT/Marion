using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marion.ApiService.Infrastructure.Persistence;

internal static class PersistenceServiceCollectionExtensions
{
  internal static IServiceCollection AddAuthPersistence(this IServiceCollection services)
  {
    services.TryAddScoped<IAuthRepository, AuthRepository>();
    return services;
  }
}
