#pragma warning disable ASPIRECERTIFICATES001

using System.Security.Cryptography;
using System.Text.Json.Nodes;

var builder = DistributedApplication.CreateBuilder(args);
var integrationTesting = string.Equals(
    builder.Configuration["IntegrationTesting"],
    "true",
    StringComparison.OrdinalIgnoreCase);

// Shared secret that lets the API distinguish the trusted BFF from any other caller.
var bffKey = builder.AddParameter(
    "bff-key",
    builder.Configuration["Parameters:bff-key"]
        ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    secret: true);

var sql = builder.AddSqlServer("sql");
if (!integrationTesting)
{
    sql.WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent);
}
var marionDb = sql.AddDatabase("mariondb");

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
var documents = storage.AddBlobContainer("documents", "test-files");

var messaging = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator(emulator =>
    {
        emulator.WithConfiguration(configuration =>
        {
            // The official emulator only supports its fixed namespace name.
            var namespaces = configuration["UserConfig"]?["Namespaces"]?.AsArray()
                ?? throw new InvalidOperationException(
                    "The Service Bus emulator namespace configuration is missing.");
            if (namespaces[0] is not JsonObject serviceBusNamespace)
            {
                throw new InvalidOperationException(
                    "The Service Bus emulator namespace configuration is invalid.");
            }

            serviceBusNamespace["Name"] = "sbemulatorns";
        });
    });


var documentProcessing = messaging.AddServiceBusQueue(
    "document-processing",
    "document-processing");
var loanEvents = messaging.AddServiceBusTopic("loan-events", "loan-events");
var loanEventsSubscription = loanEvents.AddServiceBusSubscription(
    "loan-events-subscription",
    "loan-events-subscription");

var apiService = builder.AddProject<Projects.Marion_ApiService>("apiservice")
    .WithReference(marionDb)
    .WaitFor(marionDb)
    .WithReference(documents)
    .WaitFor(documents)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithEnvironment("Auth__BffKey", bffKey)
    .WithHttpHealthCheck("/health");

if (integrationTesting)
{
    apiService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "IntegrationTesting");
}
else
{
    builder.AddViteApp("frontend", "../Marion.Web")
        .WithPnpm()
        .WithHttpsEndpoint(port: 7257, env: "PORT")
        .WithHttpsDeveloperCertificate()
        .WithExternalHttpEndpoints()
        .WithReference(apiService)
        .WaitFor(apiService)
        .WithEnvironment("NUXT_API_BASE", apiService.GetEndpoint("https"))
        .WithEnvironment("NUXT_AUTH_BFF_KEY", bffKey);
}

builder.Build().Run();
