using Azure.Core;
using Azure.Identity;
using Marion.ApiService.Infrastructure.Configuration;
using Marion.ApiService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class SqlPersistenceRegistrationTests
{
    [Fact]
    public void Local_mode_uses_the_named_SQL_connection_without_an_Entra_interceptor()
    {
        const string connectionString =
            "Data Source=local-sql;Initial Catalog=marion;Integrated Security=True;Encrypt=True";
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                [$"{PlatformOptions.SectionName}:Mode"] = "Local",
                [$"{PlatformOptions.SectionName}:Local:BlobServiceUri"] =
                    "https://storage.invalid",
                [$"{PlatformOptions.SectionName}:Local:BlobContainerName"] = "documents",
                [$"{PlatformOptions.SectionName}:Local:ServiceBusFullyQualifiedNamespace"] =
                    "messaging.invalid",
                ["ConnectionStrings:mariondb"] = connectionString
            },
            builder => builder.AddMarionPersistence());

        using var scope = host.Services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<MarionDbContext>();
        var sqlConnection = Assert.IsType<SqlConnection>(
            dbContext.Database.GetDbConnection());

        Assert.Equal("local-sql", sqlConnection.DataSource);
        Assert.Equal("marion", sqlConnection.Database);
        Assert.Null(host.Services.GetService<SqlEntraConnectionInterceptor>());
    }

    [Fact]
    public void Azure_mode_uses_the_shared_credential_and_defers_token_acquisition()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                [$"{PlatformOptions.SectionName}:Mode"] = "Azure",
                [$"{PlatformOptions.SectionName}:Azure:BlobServiceUri"] =
                    "https://documents.blob.core.windows.net",
                [$"{PlatformOptions.SectionName}:Azure:BlobContainerName"] = "documents",
                [$"{PlatformOptions.SectionName}:Azure:ServiceBusFullyQualifiedNamespace"] =
                    "messaging.servicebus.windows.net",
                [$"{PlatformOptions.SectionName}:Azure:SqlServer"] =
                    "marion.database.windows.net",
                [$"{PlatformOptions.SectionName}:Azure:SqlDatabase"] = "marion",
                [$"{PlatformOptions.SectionName}:Azure:Identity:TenantId"] = "tenant-id"
            },
            builder => builder.AddMarionPersistence());

        var tokenCredential = host.Services.GetRequiredService<TokenCredential>();
        var defaultCredential = host.Services.GetRequiredService<DefaultAzureCredential>();
        var interceptor = host.Services.GetRequiredService<SqlEntraConnectionInterceptor>();

        using var scope = host.Services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<MarionDbContext>();
        var sqlConnection = Assert.IsType<SqlConnection>(
            dbContext.Database.GetDbConnection());
        interceptor.ConfigureConnection(sqlConnection);

        Assert.Same(defaultCredential, tokenCredential);
        Assert.Same(tokenCredential, interceptor.Credential);
        Assert.Equal("marion.database.windows.net", sqlConnection.DataSource);
        Assert.Equal("marion", sqlConnection.Database);
        Assert.Contains("Encrypt=True", sqlConnection.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(sqlConnection.AccessTokenCallback);
    }

    [Fact]
    public void Azure_mode_uses_persistence_registration_from_the_program_composition_root()
    {
        using var factory = new MarionApiFactory("Testing").WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"{PlatformOptions.SectionName}:Mode", "Azure");
            builder.UseSetting(
                $"{PlatformOptions.SectionName}:Azure:BlobServiceUri",
                "https://documents.blob.core.windows.net");
            builder.UseSetting(
                $"{PlatformOptions.SectionName}:Azure:BlobContainerName",
                "documents");
            builder.UseSetting(
                $"{PlatformOptions.SectionName}:Azure:ServiceBusFullyQualifiedNamespace",
                "messaging.servicebus.windows.net");
            builder.UseSetting(
                $"{PlatformOptions.SectionName}:Azure:SqlServer",
                "marion.database.windows.net");
            builder.UseSetting(
                $"{PlatformOptions.SectionName}:Azure:SqlDatabase",
                "marion");
            builder.UseSetting(
                $"{PlatformOptions.SectionName}:Azure:Identity:TenantId",
                "tenant-id");
        });

        var tokenCredential = factory.Services.GetRequiredService<TokenCredential>();
        var interceptor = factory.Services.GetRequiredService<SqlEntraConnectionInterceptor>();

        using var scope = factory.Services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<MarionDbContext>();
        var sqlConnection = Assert.IsType<SqlConnection>(
            dbContext.Database.GetDbConnection());

        interceptor.ConfigureConnection(sqlConnection);

        Assert.Same(tokenCredential, interceptor.Credential);
        Assert.Equal("marion.database.windows.net", sqlConnection.DataSource);
        Assert.Equal("marion", sqlConnection.Database);
        Assert.Contains("Encrypt=True", sqlConnection.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(sqlConnection.AccessTokenCallback);
    }

    [Fact]
    public void Entra_connection_configuration_reuses_the_callback_across_connections()
    {
        var credential = new RecordingTokenCredential();
        var interceptor = new SqlEntraConnectionInterceptor(credential);
        using var firstConnection = new SqlConnection();
        using var secondConnection = new SqlConnection();

        interceptor.ConfigureConnection(firstConnection);
        interceptor.ConfigureConnection(secondConnection);

        Assert.NotNull(firstConnection.AccessTokenCallback);
        Assert.Same(firstConnection.AccessTokenCallback, secondConnection.AccessTokenCallback);
        Assert.Equal(0, credential.RequestCount);
    }

    [Fact]
    public void SQL_client_tracing_is_registered_by_the_Aspire_registration()
    {
        var activities = new ConcurrentBag<Activity>();
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                [$"{PlatformOptions.SectionName}:Mode"] = "Local",
                [$"{PlatformOptions.SectionName}:Local:BlobServiceUri"] =
                    "https://storage.invalid",
                [$"{PlatformOptions.SectionName}:Local:BlobContainerName"] = "documents",
                [$"{PlatformOptions.SectionName}:Local:ServiceBusFullyQualifiedNamespace"] =
                    "messaging.invalid",
                ["ConnectionStrings:mariondb"] =
                    "Data Source=local-sql;Initial Catalog=marion;Integrated Security=True;Encrypt=False"
            },
            builder =>
            {
                builder.AddServiceDefaults();
                builder.Services.AddOpenTelemetry()
                    .WithTracing(tracing =>
                        tracing.AddProcessor(new RecordingActivityProcessor(activities)));
                builder.AddMarionPersistence(settings => settings.DisableRetry = true);
            });

        _ = host.Services.GetRequiredService<TracerProvider>();
        using var activity = new ActivitySource(
                "OpenTelemetry.Instrumentation.SqlClient")
            .StartActivity("sql-client-registration-check");

        Assert.NotNull(activity);
        activity.Stop();
        Assert.Contains(
            activities,
            recordedActivity => recordedActivity.Source.Name ==
                "OpenTelemetry.Instrumentation.SqlClient");
    }

    [Fact]
    public async Task SQL_readiness_is_bounded_without_marking_liveness_as_a_dependency()
    {
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                [$"{PlatformOptions.SectionName}:Mode"] = "Local",
                [$"{PlatformOptions.SectionName}:Local:BlobServiceUri"] =
                    "https://storage.invalid",
                [$"{PlatformOptions.SectionName}:Local:BlobContainerName"] = "documents",
                [$"{PlatformOptions.SectionName}:Local:ServiceBusFullyQualifiedNamespace"] =
                    "messaging.invalid",
                ["ConnectionStrings:mariondb"] =
                    "Data Source=127.0.0.1,1;Initial Catalog=marion;Integrated Security=True;Encrypt=False;Connect Timeout=30"
            },
            builder =>
            {
                builder.AddServiceDefaults();
                builder.AddMarionPersistence(settings =>
                {
                    settings.ReadinessTimeout = TimeSpan.FromMilliseconds(100);
                    settings.DisableRetry = true;
                });
            });

        var registration = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations
            .Single(registration => registration.Name == nameof(MarionDbContext));
        var healthCheck = host.Services.GetRequiredService<HealthCheckService>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var report = await healthCheck.CheckHealthAsync(timeout.Token);

        Assert.Equal(TimeSpan.FromMilliseconds(100), registration.Timeout);
        Assert.DoesNotContain("live", registration.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            HealthStatus.Unhealthy,
            report.Entries[nameof(MarionDbContext)].Status);
        Assert.DoesNotContain(
            "Data Source",
            report.Entries[nameof(MarionDbContext)].Description ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IHost BuildHost(
        IReadOnlyDictionary<string, string?> settings,
        Action<WebApplicationBuilder> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddPlatformConfiguration();
        configure(builder);
        return builder.Build();
    }

    private sealed class RecordingActivityProcessor(ConcurrentBag<Activity> activities)
        : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => activities.Add(data);
    }

    private sealed class RecordingTokenCredential : TokenCredential
    {
        internal int RequestCount { get; private set; }

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return new AccessToken("test-token", DateTimeOffset.UtcNow.AddMinutes(5));
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return ValueTask.FromResult(
                new AccessToken("test-token", DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }
}
