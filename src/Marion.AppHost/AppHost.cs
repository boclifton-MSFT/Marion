#pragma warning disable ASPIRECERTIFICATES001

using System.Text.Json.Nodes;

var builder = DistributedApplication.CreateBuilder(args);
var integrationTesting = string.Equals(
    builder.Configuration["IntegrationTesting"],
    "true",
    StringComparison.OrdinalIgnoreCase);

var googleClientId = builder.AddParameter("GoogleClientId");
var googleClientSecret = builder.AddParameter("GoogleClientSecret", secret: true);

var keyVault = integrationTesting
    ? null
    : builder.AddAzureKeyVault("marionkv");

if (keyVault is not null)
{
    keyVault.AddSecret("google-client-id", googleClientId);
    keyVault.AddSecret("google-client-secret", googleClientSecret);
}

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
var messaging = builder.AddAzureServiceBus("messaging")
    .ClearDefaultRoleAssignments()
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

if (!integrationTesting)
{
    sql.WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent);
}

var marionDb = sql.AddDatabase("mariondb");
var documents = storage.AddBlobContainer("documents", "test-files");
var documentProcessing = messaging.AddServiceBusQueue(
    "document-processing",
    "document-processing");
var loanEvents = messaging.AddServiceBusTopic("loan-events", "loan-events");
var loanEventsSubscription = loanEvents.AddServiceBusSubscription(
    "loan-events-subscription",
    "loan-events-subscription");

var apiService = builder.AddProject<Projects.Marion_ApiService>("apiservice")
    .WithEnvironment(context =>
        context.EnvironmentVariables["Marion__Platform__Mode"] =
            context.ExecutionContext.IsPublishMode ? "Azure" : "Local")
    .WithReference(marionDb)
    .WaitFor(marionDb)
    .WithReference(documents)
    .WaitFor(documents)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health");

if (keyVault is not null)
{
    apiService.WithReference(keyVault);
}

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
        .WithReference(marionDb)
        .WaitFor(marionDb)
        .WithReference(apiService)
        .WaitFor(apiService)
        .WithEnvironment("NUXT_API_BASE", apiService.GetEndpoint("https"))
        .WithEnvironment("NUXT_AUTH_STORE_PROVISION_SCHEMA", "true");
}

builder.Build().Run();
