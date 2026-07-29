using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace Marion.ApiService.Tests;

public sealed class MarionApiFactory : WebApplicationFactory<Program>
{
    private readonly string environmentName;

    public MarionApiFactory()
        : this("Testing")
    {
    }

    internal MarionApiFactory(string environmentName)
    {
        this.environmentName = environmentName;
    }

    public string DatabaseName { get; } = $"marion-test-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = Guid.NewGuid().ToString("N"),
            InitialCatalog = DatabaseName,
            IntegratedSecurity = true,
            Encrypt = true
        }.ConnectionString;

        builder.UseEnvironment(environmentName);
        builder.UseSetting("ConnectionStrings:mariondb", connectionString);
    }
}
