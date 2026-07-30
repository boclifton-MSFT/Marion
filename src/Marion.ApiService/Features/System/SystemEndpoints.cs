using System.Reflection;
using System.Text.Json.Serialization;
using Marion.ApiService.Infrastructure.Messaging;
using Marion.ApiService.Infrastructure.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marion.ApiService.Features.System;

internal static class SystemEndpoints
{
    private static readonly string[] BuildIdEnvironmentVariables =
    [
        "GITHUB_SHA",
        "BUILD_SOURCEVERSION",
        "CI_COMMIT_SHA",
        "SOURCE_COMMIT",
        "COMMIT_SHA",
        "GIT_COMMIT",
        "VCS_REF",
        "BUILD_ID"
    ];

    internal static void MapSystemEndpoints(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment)
    {
        endpoints.MapGet("/", () => "The Marion API is running.")
            .WithName("GetRoot")
            .WithSummary("Confirms that the Marion API is running.")
            .Produces<string>(StatusCodes.Status200OK);

        endpoints.MapGet("/api/system/info", (IHostEnvironment environment) =>
            new SystemInfoResponse(
                environment.ApplicationName,
                GetApplicationVersion(),
                environment.EnvironmentName,
                GetBuildId(),
                DateTimeOffset.UtcNow))
            .WithName("GetSystemInfo")
            .WithSummary("Returns safe application and build metadata.")
            .WithDescription("Returns application metadata without configuration values, credentials, or internal infrastructure details.")
            .Produces<SystemInfoResponse>(StatusCodes.Status200OK);

        endpoints.MapGet("/api/system/dependencies", async (
            HealthCheckService healthCheckService,
            CancellationToken cancellationToken) =>
        {
            var report = await healthCheckService.CheckHealthAsync(cancellationToken);
            var dependencies = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new SystemDependencyResponse(
                    entry.Key,
                    MapDependencyState(entry.Value.Status)))
                .ToArray();

            return new SystemDependenciesResponse(DateTimeOffset.UtcNow, dependencies);
        })
            .WithName("GetSystemDependencies")
            .WithSummary("Returns the health state of application dependencies.")
            .WithDescription("Returns logical dependency names and safe health states without diagnostic details or exception data.")
            .Produces<SystemDependenciesResponse>(StatusCodes.Status200OK);

        if (environment.IsDevelopment() || environment.IsEnvironment("IntegrationTesting"))
        {
            endpoints.MapPost("/api/system/storage/verify", async (
                IDocumentStorageVerifier verifier,
                CancellationToken cancellationToken) =>
            {
                var result = await verifier.VerifyAsync(cancellationToken);
                return new StorageVerificationResponse(
                    "Healthy",
                    result.DurationMilliseconds);
            })
                .WithName("VerifyDocumentStorage")
                .WithSummary("Runs a bounded synthetic document storage verification.")
                .WithDescription("Uploads, reads, verifies, and removes unique non-sensitive synthetic content. Available only in Development and IntegrationTesting.")
                .Produces<StorageVerificationResponse>(StatusCodes.Status200OK);

            endpoints.MapPost("/api/system/messaging/publish-synthetic", async (
                IPlatformIntegrationPublisher publisher,
                CancellationToken cancellationToken) =>
            {
                var envelope = await publisher.PublishSyntheticAsync(cancellationToken);
                return new SyntheticPublishResponse(
                    envelope.MessageId,
                    envelope.CorrelationId,
                    envelope.EventType,
                    envelope.Version,
                    envelope.OccurredAtUtc);
            })
                .WithName("PublishSyntheticPlatformIntegrationRequest")
                .WithSummary("Publishes a bounded synthetic platform integration request.")
                .WithDescription("Publishes a non-sensitive synthetic request to document-processing. Available only in Development and IntegrationTesting.")
                .Produces<SyntheticPublishResponse>(StatusCodes.Status200OK);
        }
    }

    private static string GetApplicationVersion()
    {
        var assembly = typeof(SystemEndpoints).Assembly;

        return assembly.GetName().Version?.ToString()
            ?? assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
    }

    private static string? GetBuildId()
    {
        foreach (var variable in BuildIdEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        var informationalVersion = typeof(SystemEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var separatorIndex = informationalVersion?.IndexOf('+') ?? -1;

        return separatorIndex >= 0 && separatorIndex < informationalVersion!.Length - 1
            ? informationalVersion[(separatorIndex + 1)..]
            : null;
    }

    private static DependencyState MapDependencyState(HealthStatus status) =>
        status switch
        {
            HealthStatus.Healthy => DependencyState.Healthy,
            HealthStatus.Degraded => DependencyState.Degraded,
            _ => DependencyState.Unavailable
        };
}

public sealed record SystemInfoResponse(
    string ApplicationName,
    string Version,
    string Environment,
    string? BuildId,
    DateTimeOffset UtcTime);

public sealed record SystemDependenciesResponse(
    DateTimeOffset UtcTime,
    IReadOnlyList<SystemDependencyResponse> Dependencies);

public sealed record SystemDependencyResponse(
    string Name,
    DependencyState Status);

public sealed record StorageVerificationResponse(
    string Status,
    long DurationMilliseconds);

public sealed record SyntheticPublishResponse(
    string MessageId,
    string CorrelationId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAtUtc);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DependencyState
{
    Healthy,
    Degraded,
    Unavailable
}
