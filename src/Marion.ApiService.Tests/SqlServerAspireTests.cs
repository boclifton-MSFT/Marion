extern alias AppHost;

using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Marion.ApiService.Features.System;
using Marion.ApiService.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class SqlServerAspireTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task AppHost_model_wires_SQL_database_reference_and_wait()
    {
        using var cancellationSource = new CancellationTokenSource(DefaultTimeout);
        var builder = await CreateBuilderAsync(cancellationSource.Token);

        var sql = Assert.Single(
            builder.Resources.OfType<SqlServerServerResource>(),
            resource => resource.Name == "sql");
        var marionDb = Assert.Single(
            builder.Resources.OfType<SqlServerDatabaseResource>(),
            resource => resource.Name == "mariondb");
        var api = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == "apiservice");

        Assert.Same(sql, marionDb.Parent);
        Assert.Contains(
            api.Annotations.OfType<WaitAnnotation>(),
            annotation => ReferenceEquals(annotation.Resource, marionDb));

        var environmentContext = new EnvironmentCallbackContext(
            builder.ExecutionContext,
            api,
            cancellationToken: cancellationSource.Token);
        foreach (var annotation in api.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(environmentContext);
        }

        Assert.Contains(
            "ConnectionStrings__mariondb",
            environmentContext.EnvironmentVariables.Keys);
    }

    [Fact]
    public async Task Development_model_uses_persistent_SQL_storage()
    {
        using var cancellationSource = new CancellationTokenSource(DefaultTimeout);
        var builder = await CreateBuilderAsync(cancellationSource.Token);
        var sql = Assert.Single(
            builder.Resources.OfType<SqlServerServerResource>(),
            resource => resource.Name == "sql");

        Assert.Contains(
            sql.Annotations.OfType<ContainerMountAnnotation>(),
            annotation => annotation.Type == ContainerMountType.Volume);
        Assert.Contains(
            sql.Annotations.OfType<ContainerLifetimeAnnotation>(),
            annotation => annotation.Lifetime == ContainerLifetime.Persistent);
    }

    [Fact]
    public async Task Aspire_test_model_uses_ephemeral_SQL_storage()
    {
        using var cancellationSource = new CancellationTokenSource(DefaultTimeout);
        var builder = await CreateIsolatedBuilderAsync(cancellationSource.Token);
        var sql = Assert.Single(
            builder.Resources.OfType<SqlServerServerResource>(),
            resource => resource.Name == "sql");

        Assert.DoesNotContain(
            sql.Annotations.OfType<ContainerMountAnnotation>(),
            annotation => annotation.Type == ContainerMountType.Volume);
        Assert.DoesNotContain(
            sql.Annotations.OfType<ContainerLifetimeAnnotation>(),
            annotation => annotation.Lifetime == ContainerLifetime.Persistent);
    }

    [Fact]
    public async Task API_readiness_is_healthy_when_SQL_is_reachable()
    {
        using var cancellationSource = new CancellationTokenSource(DefaultTimeout);
        var builder = await CreateIsolatedBuilderAsync(cancellationSource.Token);

        await using var app = await builder.BuildAsync(cancellationSource.Token)
            .WaitAsync(DefaultTimeout, cancellationSource.Token);
        await app.StartAsync(cancellationSource.Token)
            .WaitAsync(DefaultTimeout, cancellationSource.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync(
            "sql",
            cancellationSource.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(
            "mariondb",
            cancellationSource.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(
            "apiservice",
            cancellationSource.Token);

        using var client = app.CreateHttpClient("apiservice");
        using var readinessResponse = await client.GetAsync(
            "/health",
            cancellationSource.Token);
        using var dependenciesResponse = await client.GetAsync(
            "/api/system/dependencies",
            cancellationSource.Token);
        var payload = await dependenciesResponse.Content
            .ReadFromJsonAsync<SystemDependenciesResponse>(
                cancellationSource.Token);

        Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dependenciesResponse.StatusCode);
        Assert.NotNull(payload);
        Assert.Contains(payload.Dependencies, dependency =>
            dependency.Name == nameof(MarionDbContext)
            && dependency.Status == DependencyState.Healthy);

        var body = await dependenciesResponse.Content.ReadAsStringAsync(
            cancellationSource.Token);
        SensitiveOutputAssertions.DoesNotContainSensitiveDetails(body);
    }

    private static Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        CancellationToken cancellationToken) =>
        DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Marion_AppHost>(
            [],
            static (_, settings) => settings.EnvironmentName = Environments.Development,
            cancellationToken);

    private static async Task<IDistributedApplicationTestingBuilder>
        CreateIsolatedBuilderAsync(CancellationToken cancellationToken)
    {
        var builder = await CreateBuilderAsync(cancellationToken);
        var sql = Assert.Single(
            builder.Resources.OfType<SqlServerServerResource>(),
            resource => resource.Name == "sql");

        foreach (var volume in sql.Annotations
                     .OfType<ContainerMountAnnotation>()
                     .Where(annotation => annotation.Type == ContainerMountType.Volume)
                     .ToArray())
        {
            sql.Annotations.Remove(volume);
        }

        foreach (var lifetime in sql.Annotations
                     .OfType<ContainerLifetimeAnnotation>()
                     .ToArray())
        {
            sql.Annotations.Remove(lifetime);
        }

        return builder;
    }
}
