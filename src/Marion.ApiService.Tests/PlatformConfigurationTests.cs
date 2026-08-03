using Azure.Core;
using Azure.Identity;
using Marion.ApiService.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Marion.ApiService.Tests;

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
        Assert.Null(factory.Services.GetService<DefaultAzureCredential>());
        Assert.Empty(factory.Services.GetServices<TokenCredential>());
    }

    [Fact]
    public void Azure_mode_registers_one_shared_DefaultAzureCredential()
    {
        using var factory = new MarionApiFactory("Testing").WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Marion:Platform:Mode", "Azure");
            builder.UseSetting(
                "Marion:Platform:Azure:BlobServiceUri",
                "https://documents.blob.core.windows.net");
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
                "Marion:Platform:Azure:Identity:TenantId",
                "tenant-id");
        });

        var options = factory.Services
            .GetRequiredService<IOptions<PlatformOptions>>()
            .Value;
        var credential = factory.Services.GetRequiredService<DefaultAzureCredential>();
        var tokenCredential = factory.Services.GetRequiredService<TokenCredential>();

        Assert.Equal(PlatformMode.Azure, options.Mode);
        Assert.Same(credential, tokenCredential);
        Assert.Single(factory.Services.GetServices<DefaultAzureCredential>());
        Assert.Single(factory.Services.GetServices<TokenCredential>());
        Assert.Null(options.Local.BlobServiceUri);
        Assert.Equal(
            "https://documents.blob.core.windows.net",
            options.Azure.BlobServiceUri);
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
        Assert.Contains("TenantId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Local settings are not allowed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mariondb", exception.ToString(), StringComparison.Ordinal);
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
}
