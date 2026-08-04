using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
    private readonly Action<ServiceBusMessageBatch> batchDisposer;
    private readonly SemaphoreSlim probeGate = new(1, 1);
    private readonly object cleanupLock = new();
    private Task? pendingCleanup;

    internal ServiceBusConnectivityHealthCheck(
        Func<ServiceBusSender> senderFactory,
        TimeSpan probeTimeout,
        IServiceBusProbeTimer probeTimer,
        Action<ServiceBusMessageBatch>? batchDisposer = null)
    {
        this.senderFactory = senderFactory;
        this.probeTimeout = probeTimeout;
        this.probeTimer = probeTimer;
        this.batchDisposer = batchDisposer ?? (static batch => batch.Dispose());
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ThrowIfCallerCanceled(cancellationToken);

        if (!probeGate.Wait(0))
        {
            ThrowIfCallerCanceled(cancellationToken);
            return HealthCheckResult.Unhealthy(TimeoutDescription);
        }

        ServiceBusProbeLifecycle? lifecycle = null;
        try
        {
            var sender = senderFactory();
            Task<ServiceBusMessageBatch> operation;
            try
            {
                // The SDK's link-opening path can ignore this token, so the lifecycle races it
                // against an independently owned budget and the caller's cancellation.
                operation = sender.CreateMessageBatchAsync(CancellationToken.None).AsTask();
            }
            catch (Exception exception)
            {
                operation = Task.FromException<ServiceBusMessageBatch>(exception);
            }

            lifecycle = new ServiceBusProbeLifecycle(
                sender,
                operation,
                batchDisposer,
                () => probeGate.Release());
            return await lifecycle.RunAsync(
                    probeTimeout,
                    probeTimer,
                    cancellationToken,
                    TrackCleanup)
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
        finally
        {
            if (lifecycle is null)
            {
                probeGate.Release();
            }
        }

        void TrackCleanup(Task cleanupTask)
        {
            lock (cleanupLock)
            {
                pendingCleanup = cleanupTask;
            }

            // Cleanup is designed to return a result rather than fault, but keep a final
            // observation hook for unexpected implementation or SDK failures.
            _ = cleanupTask.ContinueWith(
                static completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
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
        Task? cleanup;
        lock (cleanupLock)
        {
            cleanup = pendingCleanup;
        }

        if (cleanup is not null)
        {
            await cleanup.ConfigureAwait(false);
        }
    }

    private sealed class ServiceBusProbeLifecycle(
        ServiceBusSender sender,
        Task<ServiceBusMessageBatch> operation,
        Action<ServiceBusMessageBatch> batchDisposer,
        Action releaseProbeSlot)
    {
        private readonly object cleanupLock = new();
        private Task<ProbeCleanupResult>? cleanupTask;

        public async Task<HealthCheckResult> RunAsync(
            TimeSpan timeout,
            IServiceBusProbeTimer timer,
            CancellationToken callerToken,
            Action<Task> startCleanup)
        {
            using var timeoutCancellation = new CancellationTokenSource();
            var timeoutTask = timer.DelayAsync(timeout, timeoutCancellation.Token);
            var callerCancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, callerToken);

            try
            {
                var winner = await Task.WhenAny(
                        operation,
                        timeoutTask,
                        callerCancellationTask)
                    .ConfigureAwait(false);

                if (callerToken.IsCancellationRequested)
                {
                    _ = StartCleanup(startCleanup);
                    throw new OperationCanceledException(callerToken);
                }

                if (ReferenceEquals(winner, operation))
                {
                    return await CompleteOperationAsync(
                            timeoutTask,
                            callerToken,
                            startCleanup)
                        .ConfigureAwait(false);
                }

                if (ReferenceEquals(winner, timeoutTask))
                {
                    await timeoutTask.ConfigureAwait(false);
                    _ = StartCleanup(startCleanup);
                    callerToken.ThrowIfCancellationRequested();
                    return HealthCheckResult.Unhealthy(TimeoutDescription);
                }

                _ = StartCleanup(startCleanup);
                throw new OperationCanceledException(callerToken);
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                _ = StartCleanup(startCleanup);
                throw new OperationCanceledException(callerToken);
            }
            catch (Exception)
            {
                _ = StartCleanup(startCleanup);
                callerToken.ThrowIfCancellationRequested();
                return HealthCheckResult.Unhealthy(UnavailableDescription);
            }
            finally
            {
                timeoutCancellation.Cancel();
            }
        }

        private async Task<HealthCheckResult> CompleteOperationAsync(
            Task timeoutTask,
            CancellationToken callerToken,
            Action<Task> startCleanup)
        {
            var cleanup = StartCleanup(startCleanup);
            var callerCancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, callerToken);
            var winner = await Task.WhenAny(
                    cleanup,
                    timeoutTask,
                    callerCancellationTask)
                .ConfigureAwait(false);

            if (callerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(callerToken);
            }

            if (ReferenceEquals(winner, timeoutTask))
            {
                await timeoutTask.ConfigureAwait(false);
                callerToken.ThrowIfCancellationRequested();
                return HealthCheckResult.Unhealthy(TimeoutDescription);
            }

            if (!ReferenceEquals(winner, cleanup))
            {
                throw new OperationCanceledException(callerToken);
            }

            var cleanupResult = await cleanup.ConfigureAwait(false);
            callerToken.ThrowIfCancellationRequested();
            return cleanupResult.ToHealthCheckResult();
        }

        private Task<ProbeCleanupResult> StartCleanup(Action<Task> startCleanup)
        {
            lock (cleanupLock)
            {
                if (cleanupTask is not null)
                {
                    return cleanupTask;
                }

                cleanupTask = CleanupAsync();
                startCleanup(cleanupTask);
                return cleanupTask;
            }
        }

        private async Task<ProbeCleanupResult> CleanupAsync()
        {
            try
            {
                // Start both operations before awaiting either one. A stalled sender close
                // must not prevent observation and disposal of a late-created batch.
                var operationCleanup = ObserveOperationAndDisposeBatchAsync();
                var senderCleanup = DisposeSenderAsync();
                var failures = await Task.WhenAll(operationCleanup, senderCleanup)
                    .ConfigureAwait(false);
                return new(failures[0], failures[1]);
            }
            catch (Exception exception)
            {
                return new(exception, null);
            }
            finally
            {
                releaseProbeSlot();
            }
        }

        private async Task<Exception?> ObserveOperationAndDisposeBatchAsync()
        {
            try
            {
                var batch = await operation.ConfigureAwait(false);
                if (batch is null)
                {
                    return null;
                }

                try
                {
                    batchDisposer(batch);
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }
            catch (Exception exception)
            {
                // Awaiting the operation here observes both late success and late failure.
                return exception;
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

        private sealed record ProbeCleanupResult(
            Exception? OperationFailure,
            Exception? SenderFailure)
        {
            public HealthCheckResult ToHealthCheckResult() =>
                OperationFailure is null && SenderFailure is null
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy(UnavailableDescription);
        }
    }
}
