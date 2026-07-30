using Marion.ApiService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class PersistenceRegistrationTests : IClassFixture<MarionApiFactory>
{
    private readonly MarionApiFactory factory;

    public PersistenceRegistrationTests(MarionApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void MarionDbContext_uses_the_Aspire_SQL_Server_configuration()
    {
        using var scope = factory.Services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<MarionDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", dbContext.Database.ProviderName);
        Assert.Equal(factory.DatabaseName, dbContext.Database.GetDbConnection().Database);
    }

    [Fact]
    public void Development_readiness_includes_SQL_connectivity()
    {
        using var developmentFactory = new MarionApiFactory("Development");
        var registrations = developmentFactory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.Contains(registrations, registration => registration.Name == nameof(MarionDbContext));
    }

    [Fact]
    public void Test_factories_use_isolated_database_names()
    {
        using var otherFactory = new MarionApiFactory();

        Assert.NotEqual(factory.DatabaseName, otherFactory.DatabaseName);
    }
}
