using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace Marion.ApiService.Tests;

public sealed class MarionApiFactory : WebApplicationFactory<Program>
{
    internal static string[] IntegrationTestingArguments =>
    [
        "--IntegrationTesting=true",
        "--Parameters:GoogleClientId=integration-test-client",
        "--Parameters:GoogleClientSecret=integration-test-secret"
    ];

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
        builder.UseSetting("Marion:Platform:Mode", "Local");
        builder.UseSetting("ConnectionStrings:mariondb", connectionString);
        builder.UseSetting(
            "ConnectionStrings:documents",
            "Endpoint=https://storage.invalid;ContainerName=test-files");
        builder.UseSetting(
            "ConnectionStrings:messaging",
            "Endpoint=sb://messaging.invalid/;SharedAccessKeyName=test;SharedAccessKey=test");
        builder.UseSetting("DOCUMENTS_URI", "https://storage.invalid");
        builder.UseSetting("DOCUMENTS_BLOBCONTAINERNAME", "test-files");
        builder.UseSetting("MESSAGING_FULLYQUALIFIEDNAMESPACE", "messaging.invalid");
    }
}
