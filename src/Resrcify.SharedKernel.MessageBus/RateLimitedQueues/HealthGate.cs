using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// The health of one health-check tag, checked once for every rate-limited queue gated on it: every
/// <see cref="Interval"/>, and early when a queue asks (a request failed), at most once a second. Without it, each
/// queue ran the check itself, so an upstream's check (which may call the upstream, and count against its budget) ran
/// once per queue.
/// </summary>
internal sealed partial class HealthGate : IDisposable
{
    /// <summary>The soonest an early check can follow the last one.</summary>
    private static TimeSpan EarlyCheckSpacing => TimeSpan.FromSeconds(1);

    private readonly string _tag;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _firstCheck = NewSignal();
    private TaskCompletionSource _checked = NewSignal();
    private TaskCompletionSource _requested = NewSignal();
    private long _intervalTicks;
    private volatile bool _isHealthy;
    private readonly TimeProvider _time;

    public HealthGate(string tag, TimeSpan interval, IServiceProvider serviceProvider, ILogger logger)
    {
        _tag = tag;
        _intervalTicks = interval.Ticks;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        _ = Task.Run(() => RunAsync(_stopping.Token), _stopping.Token);
    }

    /// <summary>The checks' interval: the shortest any of its queues asked for.</summary>
    public TimeSpan Interval => TimeSpan.FromTicks(Interlocked.Read(ref _intervalTicks));

    /// <summary>The last check's verdict.</summary>
    public bool IsHealthy => _isHealthy;

    /// <summary>Completes once the first check has run.</summary>
    public Task FirstCheck => _firstCheck.Task;

    /// <summary>Completes when the next check has run; take it before reading <see cref="IsHealthy"/>.</summary>
    public Task NextCheck => Volatile.Read(ref _checked).Task;

    /// <summary>A queue joining the gate may want it checked more often.</summary>
    public void UseInterval(TimeSpan interval)
    {
        var current = Interlocked.Read(ref _intervalTicks);
        while (interval.Ticks < current)
        {
            var previous = Interlocked.CompareExchange(ref _intervalTicks, interval.Ticks, current);
            if (previous == current)
                return;
            current = previous;
        }
    }

    /// <summary>Checks soon (at most once a second): a request failed, so the upstream may be gone.</summary>
    public void RequestCheck()
        => Volatile.Read(ref _requested).TrySetResult();

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            // A fresh request signal before checking: a request failing during the check asks for another one.
            var requested = NewSignal();
            Interlocked.Exchange(ref _requested, requested);
            var checkedAt = _time.GetUtcNow();

            _isHealthy = await IsHealthyAsync(stopping);
            _firstCheck.TrySetResult();
            Interlocked.Exchange(ref _checked, NewSignal()).TrySetResult();

            if (!await WaitForNextCheckAsync(requested.Task, checkedAt, stopping))
                return;
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken stopping)
    {
        var healthChecks = _serviceProvider.GetService<HealthCheckService>();
        if (healthChecks is null)
            return true;
        try
        {
            var report = await healthChecks.CheckHealthAsync(check => check.Tags.Contains(_tag), stopping);
            return report.Status != HealthStatus.Unhealthy;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCheckFailed(exception, _tag);
            return false;
        }
    }

    /// <summary>Waits for the interval, or for an early request (not sooner than a second after the last check).</summary>
    private async Task<bool> WaitForNextCheckAsync(Task requested, DateTimeOffset checkedAt, CancellationToken stopping)
    {
        try
        {
            await Task.WhenAny(Task.Delay(Interval, _time, stopping), requested);
            stopping.ThrowIfCancellationRequested();
            var spacing = checkedAt + EarlyCheckSpacing - _time.GetUtcNow();
            if (requested.IsCompleted && spacing > TimeSpan.Zero)
                await Task.Delay(spacing, _time, stopping);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The health check for {Tag} failed to run; its queues step aside until it passes")]
    private partial void LogCheckFailed(Exception exception, string tag);
}
