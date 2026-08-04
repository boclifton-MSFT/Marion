using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Marion.ApiService.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http;
using Xunit;

namespace Marion.ApiService.Tests;

public sealed class ServiceBusConnectivityHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_uses_only_sender_batch_creation_without_sending_or_receiving()
    {
        var sender = new RecordingServiceBusSender();
        var healthCheck = CreateHealthCheck(() => sender);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, sender.BatchCreationCount);
        Assert.Equal(0, sender.SendCount);
        Assert.Equal(0, sender.ReceiveCount);
        Assert.Equal(0, sender.PeekCount);
        Assert.Equal(1, sender.DisposeCount);
        Assert.False(sender.LastBatchCreationToken.CanBeCanceled);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task CheckHealth_disposes_the_sender_when_batch_creation_fails_synchronously()
    {
        var sender = new RecordingServiceBusSender(
            new InvalidOperationException("sensitive endpoint details"),
            throwSynchronously: true);
        var healthCheck = CreateHealthCheck(() => sender);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Equal(1, sender.DisposeCount);
    }

    [Fact]
    public async Task CheckHealth_creates_a_fresh_sender_and_reports_a_later_RBAC_failure()
    {
        var firstSender = new RecordingServiceBusSender();
        var secondSender = new RecordingServiceBusSender(
            new UnauthorizedAccessException("tenant credential endpoint"));
        var senders = new Queue<ServiceBusSender>([firstSender, secondSender]);
        var healthCheck = CreateHealthCheck(() => senders.Dequeue());

        var firstResult = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);
        var secondResult = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, firstResult.Status);
        AssertSanitizedFailure(secondResult, "tenant credential endpoint");
        Assert.Equal(1, firstSender.DisposeCount);
        Assert.Equal(1, secondSender.DisposeCount);
        Assert.Equal(1, firstSender.BatchCreationCount);
        Assert.Equal(1, secondSender.BatchCreationCount);
    }

    [Fact]
    public async Task CheckHealth_returns_at_the_internal_budget_and_observes_a_late_success()
    {
        var lateOperation = new TaskCompletionSource<ServiceBusMessageBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingServiceBusSender(
            _ => new ValueTask<ServiceBusMessageBatch>(lateOperation.Task));
        var timer = new ManualProbeTimer();
        var healthCheck = CreateHealthCheck(() => sender, timer);

        var checkTask = healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);
        await sender.BatchStarted.Task;
        timer.Release();

        var result = await checkTask;

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Service Bus connectivity check timed out.", result.Description);
        Assert.Null(result.Exception);
        Assert.Equal(1, sender.DisposeCount);
        Assert.False(sender.OperationSettled.Task.IsCompleted);

        lateOperation.SetResult(null!);
        await healthCheck.WaitForPendingCleanupAsync();
        Assert.True(sender.OperationSettled.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CheckHealth_returns_at_the_internal_budget_and_observes_a_late_failure()
    {
        var lateOperation = new TaskCompletionSource<ServiceBusMessageBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingServiceBusSender(
            _ => new ValueTask<ServiceBusMessageBatch>(lateOperation.Task));
        var timer = new ManualProbeTimer();
        var healthCheck = CreateHealthCheck(() => sender, timer);

        var checkTask = healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);
        await sender.BatchStarted.Task;
        timer.Release();

        var result = await checkTask;

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Equal(1, sender.DisposeCount);

        lateOperation.SetException(new ServiceBusException(
            "late tenant endpoint amqp details",
            ServiceBusFailureReason.GeneralError,
            "document-processing",
            null));
        await healthCheck.WaitForPendingCleanupAsync();
        Assert.True(sender.OperationSettled.Task.IsCompleted);
    }

    [Fact]
    public async Task CheckHealth_caller_cancellation_wins_and_preserves_the_caller_token()
    {
        var lateOperation = new TaskCompletionSource<ServiceBusMessageBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingServiceBusSender(
            _ => new ValueTask<ServiceBusMessageBatch>(lateOperation.Task));
        var timer = new ManualProbeTimer();
        var healthCheck = CreateHealthCheck(() => sender, timer);
        using var cancellation = new CancellationTokenSource();

        var checkTask = healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            cancellation.Token);
        await sender.BatchStarted.Task;
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => checkTask);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, sender.DisposeCount);

        lateOperation.SetResult(null!);
        await healthCheck.WaitForPendingCleanupAsync();
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task CheckHealth_sanitizes_all_failure_families(
        Exception failure,
        string expectedDescription)
    {
        var sender = new RecordingServiceBusSender(failure);
        var healthCheck = CreateHealthCheck(() => sender);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(expectedDescription, result.Description);
        Assert.Null(result.Exception);
        Assert.Equal(1, sender.DisposeCount);
        Assert.DoesNotContain("tenant", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("amqp", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> Failures() =>
    [
        [new AuthenticationFailedException("tenant secret credential endpoint"),
            "Service Bus connectivity check failed."],
        [new UnauthorizedAccessException("tenant credential endpoint"),
            "Service Bus connectivity check failed."],
        [new ServiceBusException(
            "tenant credential endpoint amqp details",
            ServiceBusFailureReason.GeneralError,
            "document-processing",
            null),
            "Service Bus connectivity check failed."],
        [new RequestFailedException(403, "tenant credential endpoint"),
            "Service Bus connectivity check failed."],
        [new HttpRequestException("sensitive HTTP endpoint details"),
            "Service Bus connectivity check failed."],
        [new TimeoutException("sensitive timeout endpoint details"),
            "Service Bus connectivity check failed."],
        [new OperationCanceledException("unrelated cancellation"),
            "Service Bus connectivity check failed."],
        [new InvalidOperationException("sensitive unexpected details"),
            "Service Bus connectivity check failed."]
    ];

    private static ServiceBusConnectivityHealthCheck CreateHealthCheck(
        Func<ServiceBusSender> senderFactory,
        ManualProbeTimer? timer = null) =>
        new(
            senderFactory,
            TimeSpan.FromSeconds(1),
            timer ?? new ManualProbeTimer(releaseImmediately: true));

    private static void AssertSanitizedFailure(
        HealthCheckResult result,
        string sensitiveDiagnostics)
    {
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Service Bus connectivity check failed.", result.Description);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(
            sensitiveDiagnostics,
            result.Description,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ManualProbeTimer : IServiceBusProbeTimer
    {
        private readonly TaskCompletionSource<object?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualProbeTimer(bool releaseImmediately = false)
        {
            if (releaseImmediately)
            {
                completion.TrySetResult(null);
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            completion.Task;

        public void Release() => completion.TrySetResult(null);
    }

    private sealed class RecordingServiceBusSender : ServiceBusSender
    {
        private readonly Func<CancellationToken, ValueTask<ServiceBusMessageBatch>> operation;

        public RecordingServiceBusSender(Exception? failure = null)
            : this(_ => failure is null
                ? ValueTask.FromResult<ServiceBusMessageBatch>(null!)
                : ValueTask.FromException<ServiceBusMessageBatch>(failure))
        {
        }

        public RecordingServiceBusSender(
            Exception failure,
            bool throwSynchronously)
            : this(_ => ValueTask.FromException<ServiceBusMessageBatch>(failure))
        {
            if (throwSynchronously)
            {
                synchronousFailure = failure;
            }
        }

        public RecordingServiceBusSender(
            Func<CancellationToken, ValueTask<ServiceBusMessageBatch>> operation)
        {
            this.operation = operation;
        }

        private Exception? synchronousFailure;

        public int BatchCreationCount { get; private set; }

        public int SendCount { get; private set; }

        public int ReceiveCount { get; private set; }

        public int PeekCount { get; private set; }

        public int DisposeCount { get; private set; }

        public CancellationToken LastBatchCreationToken { get; private set; }

        public TaskCompletionSource<object?> BatchStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> OperationSettled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(
            CancellationToken cancellationToken = default)
        {
            BatchCreationCount++;
            LastBatchCreationToken = cancellationToken;
            BatchStarted.TrySetResult(null);
            if (synchronousFailure is not null)
            {
                throw synchronousFailure;
            }

            return InvokeOperationAsync(cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        public override Task SendMessagesAsync(
            IEnumerable<ServiceBusMessage> messages,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        public override Task SendMessagesAsync(
            ServiceBusMessageBatch messageBatch,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        private async ValueTask<ServiceBusMessageBatch> InvokeOperationAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                OperationSettled.TrySetResult(null);
            }
        }
    }
}
