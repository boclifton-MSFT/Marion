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
var azureSqlServer = builder.ExecutionContext.IsPublishMode
    ? builder.AddParameter("azure-sql-server")
    : null;
var azureSqlDatabase = builder.ExecutionContext.IsPublishMode
    ? builder.AddParameter("azure-sql-database")
    : null;

var apiService = builder.AddProject<Projects.Marion_ApiService>("apiservice")
    .WithEnvironment(context =>
    {
        if (context.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["Marion__Platform__Mode"] = "Local";
            return;
        }

        if (!context.ExecutionContext.IsPublishMode
            || azureSqlServer is null
            || azureSqlDatabase is null)
        {
            throw new InvalidOperationException(
                "The API publish environment requires the Azure deployment parameters.");
        }

        context.EnvironmentVariables["Marion__Platform__Mode"] = "Azure";
        context.EnvironmentVariables["Marion__Platform__Azure__BlobServiceUri"] =
            storage.Resource.BlobUriExpression;
        context.EnvironmentVariables["Marion__Platform__Azure__BlobContainerName"] =
            documents.Resource.BlobContainerName;
        context.EnvironmentVariables[
                "Marion__Platform__Azure__ServiceBusFullyQualifiedNamespace"] =
            messaging.Resource.HostName;
        context.EnvironmentVariables["Marion__Platform__Azure__SqlServer"] =
            azureSqlServer.Resource;
        context.EnvironmentVariables["Marion__Platform__Azure__SqlDatabase"] =
            azureSqlDatabase.Resource;
    })
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
