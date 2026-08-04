using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Collections.Concurrent;

namespace Marion.ApiService.Infrastructure.Messaging;

internal interface IServiceBusProbeTimer
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class ServiceBusProbeTimer(TimeProvider timeProvider) : IServiceBusProbeTimer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}

internal sealed class ServiceBusConnectivityHealthCheck : IHealthCheck
{
    private const string UnavailableDescription = "Service Bus connectivity check failed.";
    private const string TimeoutDescription = "Service Bus connectivity check timed out.";

    private readonly Func<ServiceBusSender> senderFactory;
    private readonly TimeSpan probeTimeout;
    private readonly IServiceBusProbeTimer probeTimer;
    private readonly ConcurrentDictionary<Task, byte> pendingCleanups = new();

    internal ServiceBusConnectivityHealthCheck(
        Func<ServiceBusSender> senderFactory,
        TimeSpan probeTimeout,
        IServiceBusProbeTimer probeTimer)
    {
        this.senderFactory = senderFactory;
        this.probeTimeout = probeTimeout;
        this.probeTimer = probeTimer;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ServiceBusSender? sender = null;
        Task<ServiceBusMessageBatch>? operation = null;

        try
        {
            sender = senderFactory();
            // The SDK's link-opening path can ignore this token, so the lifecycle races it
            // against an independently owned budget and the caller's cancellation.
            operation = sender.CreateMessageBatchAsync(CancellationToken.None).AsTask();
        }
        catch (Exception exception)
        {
            if (sender is null)
            {
                ThrowIfCallerCanceled(cancellationToken);
                return SanitizeFailure(exception);
            }

            operation = Task.FromException<ServiceBusMessageBatch>(exception);
        }

        var lifecycle = new ServiceBusProbeLifecycle(sender!, operation!);
        try
        {
            return await lifecycle.RunAsync(
                    probeTimeout,
                    probeTimer,
                    cancellationToken,
                    StartCleanup)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception)
        {
            ThrowIfCallerCanceled(cancellationToken);
            return SanitizeFailure(exception);
        }

        void StartCleanup(Task cleanupTask)
        {
            pendingCleanups.TryAdd(cleanupTask, 0);
            _ = cleanupTask.ContinueWith(
                static (completedTask, state) =>
                {
                    var owner = (ServiceBusConnectivityHealthCheck)state!;
                    owner.pendingCleanups.TryRemove(completedTask, out _);
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        static void ThrowIfCallerCanceled(CancellationToken callerToken)
        {
            if (callerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(callerToken);
            }
        }

        static HealthCheckResult SanitizeFailure(Exception _) =>
            HealthCheckResult.Unhealthy(UnavailableDescription);
    }

    internal async Task WaitForPendingCleanupAsync()
    {
        while (true)
        {
            var cleanups = pendingCleanups.Keys.ToArray();
            if (cleanups.Length == 0)
            {
                return;
            }

            await Task.WhenAll(cleanups).ConfigureAwait(false);
        }
    }

    private sealed class ServiceBusProbeLifecycle(
        ServiceBusSender sender,
        Task<ServiceBusMessageBatch> operation)
    {
        private readonly object disposalLock = new();
        private Task<Exception?>? disposalTask;
        private Task? cleanupTask;

        public async Task<HealthCheckResult> RunAsync(
            TimeSpan timeout,
            IServiceBusProbeTimer timer,
            CancellationToken callerToken,
            Action<Task> startCleanup)
        {
            using var timeoutCancellation = new CancellationTokenSource();
            var timeoutTask = timer.DelayAsync(timeout, timeoutCancellation.Token);
            var callerCancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, callerToken);
            var winner = await Task.WhenAny(
                    operation,
                    timeoutTask,
                    callerCancellationTask)
                .ConfigureAwait(false);

            try
            {
                if (callerToken.IsCancellationRequested)
                {
                    startCleanup(StartCleanup());
                    throw new OperationCanceledException(callerToken);
                }

                if (ReferenceEquals(winner, operation))
                {
                    return await CompleteOperationAsync(callerToken).ConfigureAwait(false);
                }

                if (ReferenceEquals(winner, timeoutTask))
                {
                    await timeoutTask.ConfigureAwait(false);
                    startCleanup(StartCleanup());
                    callerToken.ThrowIfCancellationRequested();
                    return HealthCheckResult.Unhealthy(TimeoutDescription);
                }

                startCleanup(StartCleanup());
                throw new OperationCanceledException(callerToken);
            }
            finally
            {
                timeoutCancellation.Cancel();
            }
        }

        private async Task<HealthCheckResult> CompleteOperationAsync(
            CancellationToken callerToken)
        {
            ServiceBusMessageBatch? batch = null;
            Exception? failure = null;

            try
            {
                batch = await operation.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (batch is not null)
            {
                try
                {
                    batch.Dispose();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }

            var disposalFailure = await DisposeSenderOnceAsync().ConfigureAwait(false);
            failure ??= disposalFailure;
            callerToken.ThrowIfCancellationRequested();

            return failure is null
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy(UnavailableDescription);
        }

        private Task StartCleanup()
        {
            lock (disposalLock)
            {
                return cleanupTask ??= CleanupLateOperationAsync();
            }
        }

        private async Task CleanupLateOperationAsync()
        {
            // Dispose the probe sender promptly, then observe the SDK task and any late batch.
            _ = await DisposeSenderOnceAsync().ConfigureAwait(false);
            try
            {
                var batch = await operation.ConfigureAwait(false);
                batch?.Dispose();
            }
            catch (Exception)
            {
                // The health result has already returned; this await exists to observe late SDK failures.
            }
        }

        private Task<Exception?> DisposeSenderOnceAsync()
        {
            lock (disposalLock)
            {
                return disposalTask ??= DisposeSenderAsync();
            }
        }

        private async Task<Exception?> DisposeSenderAsync()
        {
            try
            {
                await sender.DisposeAsync().ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
