using System.Data.Common;
using Azure.Core;
using Marion.ApiService.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace Marion.ApiService.Infrastructure.Persistence;

internal static class PersistenceServiceCollectionExtensions
{
    private const string ConnectionName = "mariondb";

    internal static IHostApplicationBuilder AddMarionPersistence(
        this IHostApplicationBuilder builder,
        Action<SqlPersistenceSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = new SqlPersistenceSettings();
        configureSettings?.Invoke(settings);
        settings.Validate();

        if (PlatformConfigurationExtensions.ParseMode(
                builder.Configuration[$"{PlatformOptions.SectionName}:Mode"])
            == PlatformMode.Azure)
        {
            builder.Services.AddSingleton<SqlEntraConnectionInterceptor>();
        }

        builder.Services.AddDbContextPool<MarionDbContext>(
            (serviceProvider, options) =>
            {
                var platform = serviceProvider
                    .GetRequiredService<IOptions<PlatformOptions>>()
                    .Value;
                var connectionString = ResolveConnectionString(
                    serviceProvider.GetRequiredService<IConfiguration>(),
                    platform);

                options.UseSqlServer(
                    connectionString,
                    sqlOptions =>
                    {
                        if (settings.CommandTimeoutSeconds is not null)
                        {
                            sqlOptions.CommandTimeout(settings.CommandTimeoutSeconds);
                        }

                        if (!settings.DisableRetry)
                        {
                            sqlOptions.EnableRetryOnFailure();
                        }
                    });

                if (platform.Mode == PlatformMode.Azure)
                {
                    options.AddInterceptors(
                        serviceProvider.GetRequiredService<SqlEntraConnectionInterceptor>());
                }
            });

        if (!settings.DisableHealthChecks)
        {
            builder.Services
                .AddHealthChecks()
                .AddDbContextCheck<MarionDbContext>(
                    name: nameof(MarionDbContext),
                    failureStatus: HealthStatus.Unhealthy,
                    tags: null,
                    customTestQuery: static async (dbContext, cancellationToken) =>
                    {
                        try
                        {
                            return await dbContext.Database.CanConnectAsync(cancellationToken);
                        }
                        catch (DbException)
                        {
                            return false;
                        }
                        catch (InvalidOperationException)
                        {
                            return false;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            return false;
                        }
                    });

            builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                var registration = options.Registrations
                    .SingleOrDefault(check => check.Name == nameof(MarionDbContext));
                if (registration is not null)
                {
                    registration.Timeout = settings.ReadinessTimeout;
                }
            });
        }

        return builder;
    }

    private static string ResolveConnectionString(
        IConfiguration configuration,
        PlatformOptions platform)
    {
        return platform.Mode switch
        {
            PlatformMode.Local => ResolveLocalConnectionString(configuration, platform),
            PlatformMode.Azure => BuildAzureConnectionString(platform.Azure),
            _ => throw new InvalidOperationException(
                "Marion:Platform:Mode must be either Local or Azure before persistence is registered.")
        };
    }

    private static string ResolveLocalConnectionString(
        IConfiguration configuration,
        PlatformOptions platform)
    {
        var connectionName = string.IsNullOrWhiteSpace(platform.Local.SqlConnectionName)
            ? ConnectionName
            : platform.Local.SqlConnectionName;
        var connectionString = configuration.GetConnectionString(connectionName);

        return !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException(
                $"The local SQL connection named '{connectionName}' is required.");
    }

    private static string BuildAzureConnectionString(AzurePlatformOptions options)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = options.SqlServer
                ?? throw new InvalidOperationException(
                    "Marion:Platform:Azure:SqlServer is required."),
            InitialCatalog = options.SqlDatabase
                ?? throw new InvalidOperationException(
                    "Marion:Platform:Azure:SqlDatabase is required."),
            TrustServerCertificate = false
        };
        builder["Encrypt"] = "True";

        return builder.ConnectionString;
    }
}

internal sealed class SqlPersistenceSettings
{
    internal bool DisableHealthChecks { get; set; }

    internal bool DisableRetry { get; set; }

    internal int? CommandTimeoutSeconds { get; set; }

    internal TimeSpan ReadinessTimeout { get; set; } =
        TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (ReadinessTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadinessTimeout),
                "SQL readiness timeout must be greater than zero.");
        }

        if (CommandTimeoutSeconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeoutSeconds),
                "SQL command timeout must be greater than zero.");
        }
    }
}
