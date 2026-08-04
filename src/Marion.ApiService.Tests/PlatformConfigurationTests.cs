extern alias AppHost;

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using Aspire.Hosting.Testing;
using Marion.ApiService.Features.System;
using Marion.ApiService.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using AppHostProjects = AppHost::Projects;

namespace Marion.ApiService.Tests;

[Collection(AppHostTestCollection.Name)]
public sealed class PlatformConfigurationTests
{
    [Fact]
    public void Local_mode_uses_Aspire_endpoints_without_registering_an_Azure_credential()
    {
        using var factory = new MarionApiFactory();
        var options = factory.Services
            .GetRequiredService<IOptions<PlatformOptions>>()
            .Value;

        Assert.Equal(PlatformMode.Local, options.Mode);
        Assert.Equal("test-files", options.Local.BlobContainerName);
        Assert.Equal(
            "messaging.invalid",
            options.Local.ServiceBusFullyQualifiedNamespace);
        Assert.Equal("mariondb", options.Local.SqlConnectionName);
        Assert.Null(factory.Services.GetService<ManagedIdentityCredential>());
        Assert.Null(factory.Services.GetService<DefaultAzureCredential>());
        Assert.Empty(factory.Services.GetServices<TokenCredential>());
    }

    [Fact]
    public void Local_mode_derives_platform_settings_from_named_Aspire_connections()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{PlatformOptions.SectionName}:Mode"] = "Local",
            ["ConnectionStrings:documents"] =
                "Endpoint=https://storage.named;ContainerName=named-files",
            ["ConnectionStrings:messaging"] =
                "Endpoint=sb://messaging.named/;SharedAccessKeyName=test;SharedAccessKey=test"
        });
        builder.AddPlatformConfiguration();

        using var host = builder.Build();
        var options = host.Services
            .GetRequiredService<IOptions<PlatformOptions>>()
            .Value;

        Assert.Equal("https://storage.named", options.Local.BlobServiceUri);
        Assert.Equal("named-files", options.Local.BlobContainerName);
        Assert.Equal("messaging.named", options.Local.ServiceBusFullyQualifiedNamespace);
    }

    [Fact]
    public void Missing_mode_fails_validation_without_selecting_Local()
    {
        var exception = ResolveOptions(new Dictionary<string, string?>());

        Assert.Contains("Marion:Platform:Mode", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Local settings", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_mode_registers_one_shared_deterministic_ManagedIdentityCredential()
    {
        using var factory = new MarionApiFactory("Testing").WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Marion:Platform:Mode", "Azure");
            builder.UseSetting(
                "Marion:Platform:Azure:BlobServiceUri",
                "https://documents.blob.core.windows.net/");
            builder.UseSetting(
                "Marion:Platform:Azure:BlobContainerName",
                "documents");
            builder.UseSetting(
                "Marion:Platform:Azure:ServiceBusFullyQualifiedNamespace",
                "messaging.servicebus.windows.net");
            builder.UseSetting(
                "Marion:Platform:Azure:SqlServer",
                "marion.database.windows.net");
            builder.UseSetting(
                "Marion:Platform:Azure:SqlDatabase",
                "marion");
            builder.UseSetting(
                "Marion:Platform:Azure:Identity:ManagedIdentityClientId",
                "user-assigned-client-id");
            builder.UseSetting("AZURE_CLIENT_ID", "environment-client-id");
            builder.UseSetting("AZURE_CLIENT_SECRET", "not-a-secret");
            builder.UseSetting("AZURE_TENANT_ID", "environment-tenant-id");
            builder.UseSetting(
                "AZURE_TOKEN_CREDENTIALS",
                nameof(VisualStudioCredential));
        });

        var options = factory.Services
            .GetRequiredService<IOptions<PlatformOptions>>()
            .Value;
        var credential = factory.Services.GetRequiredService<ManagedIdentityCredential>();
        var tokenCredential = factory.Services.GetRequiredService<TokenCredential>();

        Assert.Equal(PlatformMode.Azure, options.Mode);
        Assert.Same(credential, tokenCredential);
        Assert.Single(factory.Services.GetServices<ManagedIdentityCredential>());
        Assert.Single(factory.Services.GetServices<TokenCredential>());
        Assert.Null(factory.Services.GetService<DefaultAzureCredential>());
        Assert.Null(factory.Services.GetService<EnvironmentCredential>());
        Assert.Null(factory.Services.GetService<VisualStudioCredential>());
        Assert.Null(factory.Services.GetService<AzureCliCredential>());
        Assert.Null(options.Local.BlobServiceUri);
        Assert.Equal(
            "https://documents.blob.core.windows.net/",
            options.Azure.BlobServiceUri);
        Assert.Equal(
            "user-assigned-client-id",
            options.Azure.Identity.ManagedIdentityClientId);
        Assert.Null(options.Azure.Identity.TenantId);
    }

    [Fact]
    public void Invalid_mode_fails_validation_without_echoing_configuration()
    {
        var exception = ResolveOptions(new Dictionary<string, string?>
        {
            [$"{PlatformOptions.SectionName}:Mode"] = "unexpected-secret-mode"
        });

        Assert.Contains("Mode", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "unexpected-secret-mode",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Local_mode_requires_emulator_endpoints()
    {
        var exception = ResolveOptions(new Dictionary<string, string?>
        {
            [$"{PlatformOptions.SectionName}:Mode"] = "Local"
        });

        Assert.Contains("BlobServiceUri", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ServiceBusFullyQualifiedNamespace", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlConnectionName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_mode_rejects_missing_settings_and_contradictory_local_values()
    {
        var exception = ResolveOptions(new Dictionary<string, string?>
        {
            [$"{PlatformOptions.SectionName}:Mode"] = "Azure",
            [$"{PlatformOptions.SectionName}:Local:SqlConnectionName"] = "mariondb"
        });

        Assert.Contains("BlobServiceUri", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Local settings are not allowed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mariondb", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Azure_mode_rejects_secret_bearing_and_non_host_Service_Bus_namespaces()
    {
        var invalidNamespaces = new[]
        {
            "Endpoint=sb://messaging.servicebus.windows.net/;SharedAccessKeyName=sender;SharedAccessKey=redacted",
            "SharedAccessSignature",
            "sb://messaging.servicebus.windows.net",
            "https://messaging.servicebus.windows.net",
            "identity@messaging.servicebus.windows.net",
            "messaging.servicebus.windows.net/entity",
            "messaging.servicebus.windows.net?sig=redacted",
            "messaging.servicebus.windows.net#fragment",
            "not a host"
        };

        foreach (var invalidNamespace in invalidNamespaces)
        {
            var settings = CreateAzureSettings(
                "https://documents.blob.core.windows.net");
            settings[$"{PlatformOptions.SectionName}:Azure:ServiceBusFullyQualifiedNamespace"] =
                invalidNamespace;

            var exception = ResolveOptions(settings);

            Assert.Contains(
                "Marion:Platform:Azure:ServiceBusFullyQualifiedNamespace",
                exception.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                invalidNamespace,
                exception.ToString(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Azure_mode_rejects_Service_Bus_namespace_boundary_whitespace()
    {
        const string settingName =
            "Marion:Platform:Azure:ServiceBusFullyQualifiedNamespace";
        const string fullyQualifiedNamespace =
            " messaging.servicebus.windows.net ";
        var settings = CreateAzureSettings(
            "https://documents.blob.core.windows.net");
        settings[settingName] = fullyQualifiedNamespace;

        var exception = ResolveOptions(settings);

        Assert.Equal(
            [
                $"{settingName} must be a credential-free Service Bus fully qualified namespace host in Azure mode."
            ],
            exception.Failures);
        Assert.DoesNotContain(
            fullyQualifiedNamespace.Trim(),
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("messaging.servicebus.windows.net")]
    [InlineData("messaging.servicebus.usgovcloudapi.net")]
    [InlineData("broker.private.contoso.internal")]
    public async Task Azure_mode_accepts_Service_Bus_hosts_supported_by_the_SDK(
        string fullyQualifiedNamespace)
    {
        var settings = CreateAzureSettings(
            "https://documents.blob.core.windows.net");
        settings[$"{PlatformOptions.SectionName}:Azure:ServiceBusFullyQualifiedNamespace"] =
            fullyQualifiedNamespace;

        var options = ResolvePlatformOptions(settings);
        await using var client = new ServiceBusClient(
            fullyQualifiedNamespace,
            new ProbeTokenCredential());

        Assert.Equal(
            fullyQualifiedNamespace,
            options.Azure.ServiceBusFullyQualifiedNamespace);
        Assert.Equal(fullyQualifiedNamespace, client.FullyQualifiedNamespace);
    }

    [Fact]
    public void Azure_mode_rejects_credential_bearing_and_malformed_Blob_service_endpoints()
    {
        var invalidEndpoints = new[]
        {
            "https://documents.blob.core.windows.net/?sv=2026-01-01&sig=redacted",
            "https://identity@documents.blob.core.windows.net",
            "https://documents.blob.core.windows.net/container",
            "https://documents.blob.core.windows.net/#fragment",
            "not-a-uri"
        };

        foreach (var invalidEndpoint in invalidEndpoints)
        {
            var exception = ResolveOptions(CreateAzureSettings(invalidEndpoint));

            Assert.Contains(
                "Marion:Platform:Azure:BlobServiceUri",
                exception.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                invalidEndpoint,
                exception.ToString(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Azure_mode_Key_Vault_uses_the_shared_credential_without_startup_token_acquisition()
    {
        var credential = new ProbeTokenCredential();
        using var factory = new MarionApiFactory("Testing").WithWebHostBuilder(builder =>
        {
            foreach (var setting in CreateAzureSettings(
                "https://documents.blob.core.windows.net"))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting(
                "ConnectionStrings:marionkv",
                "https://credential-probe.vault.azure.net");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TokenCredential>();
                services.AddSingleton<TokenCredential>(credential);
                services.PostConfigureAll<SecretClientOptions>(options =>
                    options.Transport = new KeyVaultChallengeTransport());
            });
        });

        var client = factory.Services.GetRequiredService<SecretClient>();

        Assert.Same(
            credential,
            factory.Services.GetRequiredService<TokenCredential>());
        Assert.Equal(0, credential.RequestCount);

        var exception = await Record.ExceptionAsync(async () =>
            await client.GetSecretAsync("credential-probe"));

        Assert.True(credential.RequestCount > 0, exception?.ToString());
    }

    [Fact]
    public async Task AppHost_named_references_start_the_API_with_local_platform_configuration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<
            AppHostProjects.Marion_AppHost>(
            MarionApiFactory.IntegrationTestingArguments,
            timeout.Token);

        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", timeout.Token);

        using var client = app.CreateHttpClient("apiservice", "http");
        using var response = await client.GetAsync(
            "/api/system/dependencies",
            timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dependencies = await response.Content.ReadFromJsonAsync<
            SystemDependenciesResponse>(timeout.Token);
        Assert.NotNull(dependencies);
        Assert.Contains(
            dependencies.Dependencies,
            dependency => dependency.Name == "documents");
        Assert.Contains(
            dependencies.Dependencies,
            dependency => dependency.Name == "Azure_ServiceBusClient");
    }

    private static OptionsValidationException ResolveOptions(
        IReadOnlyDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddPlatformConfiguration();

        using var host = builder.Build();
        return Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<IOptions<PlatformOptions>>().Value);
    }

    private static PlatformOptions ResolvePlatformOptions(
        IReadOnlyDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddPlatformConfiguration();
        using var host = builder.Build();
        return host.Services.GetRequiredService<IOptions<PlatformOptions>>().Value;
    }

    private static Dictionary<string, string?> CreateAzureSettings(
        string blobServiceUri) =>
        new()
        {
            [$"{PlatformOptions.SectionName}:Mode"] = "Azure",
            [$"{PlatformOptions.SectionName}:Azure:BlobServiceUri"] = blobServiceUri,
            [$"{PlatformOptions.SectionName}:Azure:BlobContainerName"] = "documents",
            [$"{PlatformOptions.SectionName}:Azure:ServiceBusFullyQualifiedNamespace"] =
                "messaging.servicebus.windows.net",
            [$"{PlatformOptions.SectionName}:Azure:SqlServer"] =
                "marion.database.windows.net",
            [$"{PlatformOptions.SectionName}:Azure:SqlDatabase"] = "marion"
        };

    private sealed class ProbeTokenCredential : TokenCredential
    {
        internal int RequestCount { get; private set; }

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("Credential probe completed.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("Credential probe completed.");
        }
    }

    private sealed class KeyVaultChallengeTransport : HttpPipelineTransport
    {
        private readonly HttpPipelineTransport requestFactory = new HttpClientTransport();

        public override Request CreateRequest() => requestFactory.CreateRequest();

        public override void Process(HttpMessage message) =>
            message.Response = new KeyVaultChallengeResponse();

        public override ValueTask ProcessAsync(HttpMessage message)
        {
            Process(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class KeyVaultChallengeResponse : Response
    {
        private const string Challenge =
            "Bearer authorization=\"https://login.microsoftonline.com/tenant\", "
            + "resource=\"https://vault.azure.net\"";

        public override int Status => 401;

        public override string ReasonPhrase => "Unauthorized";

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        protected override bool TryGetHeader(
            string name,
            [NotNullWhen(true)] out string? value)
        {
            value = string.Equals(
                name,
                "WWW-Authenticate",
                StringComparison.OrdinalIgnoreCase)
                ? Challenge
                : null;
            return value is not null;
        }

        protected override bool TryGetHeaderValues(
            string name,
            [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            if (TryGetHeader(name, out var value))
            {
                values = [value];
                return true;
            }

            values = null;
            return false;
        }

        protected override bool ContainsHeader(string name) =>
            string.Equals(
                name,
                "WWW-Authenticate",
                StringComparison.OrdinalIgnoreCase);

        protected override IEnumerable<HttpHeader> EnumerateHeaders() =>
            [new HttpHeader("WWW-Authenticate", Challenge)];

        public override void Dispose()
        {
            ContentStream?.Dispose();
            ContentStream = null;
        }
    }
}
