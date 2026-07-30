#pragma warning disable ASPIRECERTIFICATES001

var builder = DistributedApplication.CreateBuilder(args);
var integrationTesting = string.Equals(
    builder.Configuration["IntegrationTesting"],
    "true",
    StringComparison.OrdinalIgnoreCase);

var sql = builder.AddSqlServer("sql");
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator =>
    {
        if (!integrationTesting)
        {
            emulator.WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent);
        }
        else
        {
            emulator.WithLifetime(ContainerLifetime.Session);
        }
    });

if (!integrationTesting)
{
    sql.WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent);
}

var marionDb = sql.AddDatabase("mariondb");
var documents = storage.AddBlobContainer("documents", "test-files");

var apiService = builder.AddProject<Projects.Marion_ApiService>("apiservice")
    .WithReference(marionDb)
    .WaitFor(marionDb)
    .WithReference(documents)
    .WaitFor(documents)
    .WithHttpHealthCheck("/health");

if (integrationTesting)
{
    apiService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "IntegrationTesting");
}
else
{
    builder.AddViteApp("frontend", "../Marion.Web")
        .WithPnpm()
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .WithReference(apiService)
        .WaitFor(apiService)
        .WithEnvironment("NUXT_API_BASE", apiService.GetEndpoint("https"));
}

builder.Build().Run();
