using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>How the backlog of <typeparamref name="TDbContext"/>'s outbox is measured.</summary>
/// <param name="MaxRetryCount">The tries a regular message gets; a lane message gets its lane's.</param>
/// <param name="Interval">How often the backlog is measured.</param>
internal sealed record OutboxBacklogSettings<TDbContext>(int MaxRetryCount, TimeSpan Interval)
    where TDbContext : DbContext
{
    /// <summary>The DbContext these settings belong to (the type parameter keys them in DI).</summary>
    public static Type DbContextType => typeof(TDbContext);
}

/// <summary>
/// Measures the outbox's backlog every <see cref="OutboxBacklogSettings{TDbContext}.Interval"/>: how many messages
/// wait, how many gave up, and how long the oldest waiting one has waited. The gauges and the health check read the
/// last measurement, so neither queries the database itself.
/// </summary>
internal sealed partial class OutboxBacklogMonitor<TDbContext>(
    IServiceScopeFactory scopeFactory,
    OutboxBacklogSettings<TDbContext> settings,
    ILogger<OutboxBacklogMonitor<TDbContext>> logger,
    TimeProvider? timeProvider = null)
    : BackgroundService
    where TDbContext : DbContext
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private OutboxBacklog? _latest;

    /// <summary>The last measurement; <see langword="null"/> until the first one.</summary>
    public OutboxBacklog? Latest => Volatile.Read(ref _latest);

    /// <summary>How often the backlog is measured.</summary>
    public TimeSpan Interval => settings.Interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var context = typeof(TDbContext).Name;
        OutboxDiagnostics.TrackBacklog(context, () => Latest);
        using var timer = new PeriodicTimer(settings.Interval, _time);
        try
        {
            do
            {
                await MeasureOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            OutboxDiagnostics.StopTrackingBacklog(context);
        }
    }

    /// <summary>Measures the backlog once; a failure is logged and the last measurement kept (it goes stale).</summary>
    internal async Task MeasureOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            Volatile.Write(ref _latest, await MeasureAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogMeasureFailed(exception, typeof(TDbContext).Name);
        }
    }

    private async Task<OutboxBacklog> MeasureAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var laneTypes = provider.GetService<OutboxLaneRegistry>()?.LaneEventTypes ?? [];
        var laneMaxRetryCount = provider.GetService<OutboxLaneSettings<TDbContext>>()?.Options.MaxRetryCount
            ?? settings.MaxRetryCount;
        var maxRetryCount = settings.MaxRetryCount;

        var messages = provider.GetRequiredService<TDbContext>()
            .Set<OutboxMessage>()
            .AsNoTracking();
        var unprocessed = messages.Where(m => m.ProcessedOnUtc == null);
        // A lane's message is tried as often as its lane allows, any other as often as the regular job does.
        var waiting = unprocessed.Where(m => laneTypes.Contains(m.Type)
            ? m.RetryCount < laneMaxRetryCount
            : m.RetryCount < maxRetryCount);
        // Marked when its last try failed (an equality seek on the processed index).
        var givenUp = messages.Where(m => m.ProcessedOnUtc == OutboxMessage.GivenUpProcessedOnUtc);

        var waitingCount = await waiting.LongCountAsync(cancellationToken);
        var unprocessedCount = await unprocessed.LongCountAsync(cancellationToken);
        var givenUpCount = await givenUp.LongCountAsync(cancellationToken);
        var oldestOccurredOnUtc = await waiting.MinAsync(m => (DateTime?)m.OccurredOnUtc, cancellationToken);

        var now = _time.GetUtcNow();
        var oldestAge = oldestOccurredOnUtc is { } occurred
            ? now.UtcDateTime - DateTime.SpecifyKind(occurred, DateTimeKind.Utc)
            : TimeSpan.Zero;
        // Also given up: unprocessed messages out of tries but not marked (they gave up before the marker existed,
        // or the retry limit was lowered since).
        return new OutboxBacklog(
            waitingCount,
            givenUpCount + unprocessedCount - waitingCount,
            oldestAge > TimeSpan.Zero ? oldestAge : TimeSpan.Zero,
            now);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not measure the {DbContext} outbox's backlog; the last measurement goes stale")]
    private partial void LogMeasureFailed(Exception exception, string dbContext);
}
