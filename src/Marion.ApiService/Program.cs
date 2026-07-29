using Marion.ApiService.Features.System;
using Marion.ApiService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var integrationTesting = builder.Environment.IsEnvironment("IntegrationTesting");

if (integrationTesting)
{
    var connectionString = builder.Configuration.GetConnectionString("mariondb")
        ?? throw new InvalidOperationException(
            "The mariondb connection string is required for integration testing.");
    builder.Configuration["ConnectionStrings:mariondb"] = new SqlConnectionStringBuilder(connectionString)
    {
        ConnectTimeout = 3,
        ConnectRetryCount = 0
    }.ConnectionString;
}

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddSqlServerDbContext<MarionDbContext>(
    "mariondb",
    settings =>
    {
        settings.DisableHealthChecks = builder.Environment.IsEnvironment("Testing");
        settings.DisableRetry = integrationTesting;
        settings.CommandTimeout = integrationTesting ? 3 : null;
    });

if (integrationTesting)
{
    builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
        options.Registrations
            .Single(registration => registration.Name == nameof(MarionDbContext))
            .Timeout = TimeSpan.FromSeconds(5));
}

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapSystemEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program
{
}
