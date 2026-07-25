#pragma warning disable ASPIRECERTIFICATES001

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.Marion_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

var frontend = builder.AddViteApp("frontend", "../Marion.Web")
    .WithPnpm()
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithEnvironment("NUXT_API_BASE", apiService.GetEndpoint("https"));

builder.Build().Run();
