using Azure.Core;
using Azure.Identity;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marion.ApiService.Infrastructure.Configuration;

public enum PlatformMode
{
    Unknown = 0,
    Local = 1,
    Azure = 2
}

public sealed class PlatformOptions
{
    public const string SectionName = "Marion:Platform";

    public PlatformMode Mode { get; set; } = PlatformMode.Unknown;

    public LocalPlatformOptions Local { get; set; } = new();

    public AzurePlatformOptions Azure { get; set; } = new();
}

public sealed class LocalPlatformOptions
{
    public string? BlobServiceUri { get; set; }

    public string? BlobContainerName { get; set; }

    public string? ServiceBusFullyQualifiedNamespace { get; set; }

    public string? SqlConnectionName { get; set; }
}

public sealed class AzurePlatformOptions
{
    public string? BlobServiceUri { get; set; }

    public string? BlobContainerName { get; set; }

    public string? ServiceBusFullyQualifiedNamespace { get; set; }

    public string? SqlServer { get; set; }

    public string? SqlDatabase { get; set; }

    public AzureIdentityOptions Identity { get; set; } = new();
}

public sealed class AzureIdentityOptions
{
    public string? TenantId { get; set; }

    public string? ManagedIdentityClientId { get; set; }
}

internal static class PlatformConfigurationExtensions
{
    private const string LocalBlobResourceName = "documents";
    private const string LocalMessagingResourceName = "messaging";
    private const string LocalBlobServiceUriEnvironmentVariable = "DOCUMENTS_URI";
    private const string LocalBlobContainerNameEnvironmentVariable = "DOCUMENTS_BLOBCONTAINERNAME";
    private const string LocalServiceBusNamespaceEnvironmentVariable =
        "MESSAGING_FULLYQUALIFIEDNAMESPACE";
    private const string LocalSqlConnectionName = "mariondb";

    internal static IHostApplicationBuilder AddPlatformConfiguration(
        this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IValidateOptions<PlatformOptions>, PlatformOptionsValidator>();
        builder.Services.AddOptions<PlatformOptions>()
            .Configure<IConfiguration>(ConfigureOptions)
            .ValidateOnStart();

        if (ParseMode(builder.Configuration[PlatformOptions.SectionName + ":Mode"])
            == PlatformMode.Azure)
        {
            builder.Services.AddSingleton<ManagedIdentityCredential>(serviceProvider =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<PlatformOptions>>()
                    .Value;
                var clientId = options.Azure.Identity.ManagedIdentityClientId;

                return string.IsNullOrWhiteSpace(clientId)
                    ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
                    : new ManagedIdentityCredential(
                        ManagedIdentityId.FromUserAssignedClientId(clientId.Trim()));
            });
            builder.Services.AddSingleton<TokenCredential>(serviceProvider =>
                serviceProvider.GetRequiredService<ManagedIdentityCredential>());
        }

        return builder;
    }

    internal static PlatformMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PlatformMode.Unknown;
        }

        var mode = value.Trim();
        return string.Equals(mode, nameof(PlatformMode.Local), StringComparison.OrdinalIgnoreCase)
            ? PlatformMode.Local
            : string.Equals(mode, nameof(PlatformMode.Azure), StringComparison.OrdinalIgnoreCase)
                ? PlatformMode.Azure
                : PlatformMode.Unknown;
    }

    private static void ConfigureOptions(
        PlatformOptions options,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(PlatformOptions.SectionName);
        options.Mode = ParseMode(section["Mode"]);
        section.GetSection(nameof(PlatformOptions.Local)).Bind(options.Local);
        section.GetSection(nameof(PlatformOptions.Azure)).Bind(options.Azure);

        if (options.Mode != PlatformMode.Azure)
        {
            options.Local.BlobServiceUri ??=
                GetBlobServiceUri(configuration);
            options.Local.BlobContainerName ??=
                GetConnectionStringValue(
                    configuration,
                    LocalBlobResourceName,
                    "ContainerName")
                ?? configuration[LocalBlobContainerNameEnvironmentVariable];
            options.Local.ServiceBusFullyQualifiedNamespace ??=
                GetServiceBusNamespace(configuration);
            options.Local.SqlConnectionName ??= LocalSqlConnectionName;
        }
    }

    private static string? GetBlobServiceUri(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(LocalBlobResourceName);
        var serviceUri = GetConnectionStringValue(connectionString, "BlobEndpoint")
            ?? GetConnectionStringValue(connectionString, "Endpoint");

        return serviceUri ?? configuration[LocalBlobServiceUriEnvironmentVariable];
    }

    private static string? GetServiceBusNamespace(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(LocalMessagingResourceName);
        var namespaceValue = GetConnectionStringValue(connectionString, "FullyQualifiedNamespace");
        if (!string.IsNullOrWhiteSpace(namespaceValue))
        {
            return namespaceValue;
        }

        var endpoint = GetConnectionStringValue(connectionString, "Endpoint");
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            && endpointUri is not null)
        {
            return endpointUri.Host;
        }

        return configuration[LocalServiceBusNamespaceEnvironmentVariable];
    }

    private static string? GetConnectionStringValue(
        IConfiguration configuration,
        string connectionName,
        string propertyName) =>
        GetConnectionStringValue(configuration.GetConnectionString(connectionName), propertyName);

    private static string? GetConnectionStringValue(
        string? connectionString,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var value = connectionString!.Trim();
        if (!value.Contains('='))
        {
            return null;
        }

        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = value;
        }
        catch (ArgumentException)
        {
            return null;
        }

        return builder.TryGetValue(propertyName, out var propertyValue)
            ? propertyValue?.ToString()
            : null;
    }
}

internal sealed class PlatformOptionsValidator : IValidateOptions<PlatformOptions>
{
    private static readonly string[] ServiceBusConnectionStringKeys =
    [
        "Endpoint",
        "EntityPath",
        "SharedAccessKey",
        "SharedAccessKeyName",
        "SharedAccessSignature"
    ];

    public ValidateOptionsResult Validate(
        string? name,
        PlatformOptions options)
    {
        var failures = options.Mode switch
        {
            PlatformMode.Local => ValidateLocal(options),
            PlatformMode.Azure => ValidateAzure(options),
            _ => ["Marion:Platform:Mode must be either Local or Azure."]
        };

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static List<string> ValidateLocal(PlatformOptions options)
    {
        var failures = new List<string>();
        var local = options.Local;

        RequireAbsoluteUri(
            failures,
            "Marion:Platform:Local:BlobServiceUri",
            local.BlobServiceUri);
        RequireValue(
            failures,
            "Marion:Platform:Local:BlobContainerName",
            local.BlobContainerName);
        RequireValue(
            failures,
            "Marion:Platform:Local:ServiceBusFullyQualifiedNamespace",
            local.ServiceBusFullyQualifiedNamespace);
        RequireValue(
            failures,
            "Marion:Platform:Local:SqlConnectionName",
            local.SqlConnectionName);

        if (HasAzureSettings(options.Azure))
        {
            failures.Add(
                "Marion:Platform:Azure settings are not allowed when Mode is Local.");
        }

        return failures;
    }

    private static List<string> ValidateAzure(PlatformOptions options)
    {
        var failures = new List<string>();
        var azure = options.Azure;

        RequireBlobServiceUri(
            failures,
            "Marion:Platform:Azure:BlobServiceUri",
            azure.BlobServiceUri);
        RequireValue(
            failures,
            "Marion:Platform:Azure:BlobContainerName",
            azure.BlobContainerName);
        RequireServiceBusFullyQualifiedNamespace(
            failures,
            "Marion:Platform:Azure:ServiceBusFullyQualifiedNamespace",
            azure.ServiceBusFullyQualifiedNamespace);
        RequireValue(
            failures,
            "Marion:Platform:Azure:SqlServer",
            azure.SqlServer);
        RequireValue(
            failures,
            "Marion:Platform:Azure:SqlDatabase",
            azure.SqlDatabase);
        if (HasLocalSettings(options.Local))
        {
            failures.Add(
                "Marion:Platform:Local settings are not allowed when Mode is Azure.");
        }

        return failures;
    }

    private static bool HasLocalSettings(LocalPlatformOptions options) =>
        HasValue(options.BlobServiceUri)
        || HasValue(options.BlobContainerName)
        || HasValue(options.ServiceBusFullyQualifiedNamespace)
        || HasValue(options.SqlConnectionName);

    private static bool HasAzureSettings(AzurePlatformOptions options) =>
        HasValue(options.BlobServiceUri)
        || HasValue(options.BlobContainerName)
        || HasValue(options.ServiceBusFullyQualifiedNamespace)
        || HasValue(options.SqlServer)
        || HasValue(options.SqlDatabase)
        || HasValue(options.Identity.TenantId)
        || HasValue(options.Identity.ManagedIdentityClientId);

    private static void RequireValue(
        ICollection<string> failures,
        string settingName,
        string? value)
    {
        if (!HasValue(value))
        {
            failures.Add($"{settingName} is required for the selected platform mode.");
        }
    }

    private static void RequireAbsoluteUri(
        ICollection<string> failures,
        string settingName,
        string? value)
    {
        if (!TryCreateUri(value, out _))
        {
            failures.Add($"{settingName} must be an absolute HTTP(S) URI.");
        }
    }

    private static void RequireBlobServiceUri(
        ICollection<string> failures,
        string settingName,
        string? value)
    {
        if (!TryCreateUri(value, out var uri)
            || uri is null
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
        {
            failures.Add(
                $"{settingName} must be a credential-free HTTPS Blob service root URI in Azure mode.");
        }
    }

    private static void RequireServiceBusFullyQualifiedNamespace(
        ICollection<string> failures,
        string settingName,
        string? value)
    {
        var fullyQualifiedNamespace = value?.Trim();
        if (string.IsNullOrEmpty(fullyQualifiedNamespace)
            || !string.Equals(value, fullyQualifiedNamespace, StringComparison.Ordinal)
            || fullyQualifiedNamespace.Any(char.IsWhiteSpace)
            || fullyQualifiedNamespace.IndexOfAny([';', '=', '/', '\\', '@', '?', '#']) >= 0
            || ServiceBusConnectionStringKeys.Contains(
                fullyQualifiedNamespace,
                StringComparer.OrdinalIgnoreCase)
            || Uri.CheckHostName(fullyQualifiedNamespace) == UriHostNameType.Unknown)
        {
            failures.Add(
                $"{settingName} must be a credential-free Service Bus fully qualified namespace host in Azure mode.");
        }
    }

    private static bool TryCreateUri(string? value, out Uri? uri) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out uri)
        && uri is not null
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);
}
