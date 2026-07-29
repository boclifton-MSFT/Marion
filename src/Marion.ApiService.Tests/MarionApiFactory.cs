using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace Marion.ApiService.Tests;

public sealed class MarionApiFactory : WebApplicationFactory<Program>
{
    private const string SqlServerSettingsPath =
        "Aspire:Microsoft:EntityFrameworkCore:SqlServer:MarionDbContext";

    private readonly bool disableHealthChecks;
    private readonly string environmentName;

    public MarionApiFactory()
        : this("Testing", disableHealthChecks: true)
    {
    }

    internal MarionApiFactory(string environmentName, bool disableHealthChecks = false)
    {
        this.environmentName = environmentName;
        this.disableHealthChecks = disableHealthChecks;
    }

    public string DatabaseName { get; } = $"marion-test-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = Guid.NewGuid().ToString("N"),
            InitialCatalog = DatabaseName,
            IntegratedSecurity = true,
            Encrypt = true,
            ConnectTimeout = 1,
            Pooling = false
        }.ConnectionString;

        builder.UseEnvironment(environmentName);
        builder.UseSetting("ConnectionStrings:mariondb", connectionString);
        builder.UseSetting(
            $"{SqlServerSettingsPath}:DisableHealthChecks",
            disableHealthChecks.ToString());
        builder.UseSetting($"{SqlServerSettingsPath}:MaxRetryCount", "0");
    }
}
