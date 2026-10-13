using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>The outbox now: messages waiting (and retrying) and messages that gave up, in all and per event type.</summary>
/// <param name="OldestWaitingOccurredOnUtc">When the oldest waiting message's event occurred; <see langword="null"/> when none waits.</param>
public sealed record OutboxSummary(
    int Waiting,
    int Retrying,
    int GivenUp,
    DateTime? OldestWaitingOccurredOnUtc,
    IReadOnlyList<OutboxEventTypeSummary> EventTypes);

/// <summary>One event type's messages; <paramref name="Lane"/> is its outbox lane, <see langword="null"/> for the regular job.</summary>
public sealed record OutboxEventTypeSummary(string Type, string? Lane, int Waiting, int Retrying, int GivenUp);

/// <summary>A message that gave up, without its content.</summary>
public sealed record OutboxMessageSummary(Guid Id, string Type, DateTime OccurredOnUtc, int RetryCount, string? Error);

/// <summary>A message with its content and where it stands.</summary>
public sealed record OutboxMessageDetails(
    Guid Id,
    string Type,
    string Content,
    DateTime OccurredOnUtc,
    DateTime? ProcessedOnUtc,
    bool GaveUp,
    int RetryCount,
    DateTime? NextAttemptOnUtc,
    string? Error);

/// <summary>
/// Looks into <typeparamref name="TDbContext"/>'s outbox and tries messages that gave up again: what an operator needs
/// instead of hand-written SQL. Registered (scoped) by <c>AddOutboxProcessing</c> and <c>AddOutboxLanes</c>; the
/// Resrcify.SharedKernel.UnitOfWork.Web package maps it to endpoints.
/// </summary>
public sealed class OutboxAdministration<TDbContext>(TDbContext context, IServiceProvider services)
    where TDbContext : DbContext
{
    /// <summary>The most messages <see cref="ListGivenUpAsync"/> returns at once.</summary>
    public const int MaxTake = 500;

    /// <summary>Counts of the messages waiting, retrying (waiting after a failed try) and given up, per event type.</summary>
    public async Task<OutboxSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context
            .Set<OutboxMessage>()
            .AsNoTracking()
            .Where(message => message.ProcessedOnUtc == null || message.ProcessedOnUtc == OutboxMessage.GivenUpProcessedOnUtc)
            .GroupBy(message => message.Type)
            .Select(type => new
            {
                Type = type.Key,
                Waiting = type.Count(message => message.ProcessedOnUtc == null),
                Retrying = type.Count(message => message.ProcessedOnUtc == null && message.RetryCount > 0),
                GivenUp = type.Count(message => message.ProcessedOnUtc == OutboxMessage.GivenUpProcessedOnUtc),
                OldestWaiting = type.Where(message => message.ProcessedOnUtc == null).Min(message => (DateTime?)message.OccurredOnUtc),
            })
            .ToListAsync(cancellationToken);

        var lanes = LaneOfEachType();
        var eventTypes = rows
            .OrderBy(row => row.Type, StringComparer.Ordinal)
            .Select(row => new OutboxEventTypeSummary(
                row.Type,
                lanes.GetValueOrDefault(row.Type),
                row.Waiting,
                row.Retrying,
                row.GivenUp))
            .ToList();
        return new OutboxSummary(
            rows.Sum(row => row.Waiting),
            rows.Sum(row => row.Retrying),
            rows.Sum(row => row.GivenUp),
            rows.Min(row => row.OldestWaiting),
            eventTypes);
    }

    /// <summary>The messages that gave up, the most recent events first; only <paramref name="type"/>'s when given.</summary>
    public async Task<IReadOnlyList<OutboxMessageSummary>> ListGivenUpAsync(
        int take = 50,
        string? type = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxTake);
        return await GivenUpMessages(type)
            .AsNoTracking()
            .OrderByDescending(message => message.OccurredOnUtc)
            .Take(take)
            .Select(message => new OutboxMessageSummary(message.Id, message.Type, message.OccurredOnUtc, message.RetryCount, message.Error))
            .ToListAsync(cancellationToken);
    }

    /// <summary>A message with its content; <see langword="null"/> when there is none with <paramref name="id"/> (or it was cleaned up).</summary>
    public async Task<OutboxMessageDetails?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => await context
            .Set<OutboxMessage>()
            .AsNoTracking()
            .Where(message => message.Id == id)
            .Select(message => new OutboxMessageDetails(
                message.Id,
                message.Type,
                message.Content,
                message.OccurredOnUtc,
                message.ProcessedOnUtc,
                message.ProcessedOnUtc == OutboxMessage.GivenUpProcessedOnUtc,
                message.RetryCount,
                message.NextAttemptOnUtc,
                message.Error))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Tries a message that gave up again, from its first try (its last error is kept until the next one), and wakes the
    /// outbox. <see langword="false"/> when no message with <paramref name="id"/> gave up.
    /// </summary>
    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default)
        => await RetryAsync(GivenUpMessages(type: null).Where(message => message.Id == id), cancellationToken) == 1;

    /// <summary>Tries every message that gave up again (only <paramref name="type"/>'s when given); how many.</summary>
    public Task<int> RetryAllGivenUpAsync(string? type = null, CancellationToken cancellationToken = default)
        => RetryAsync(GivenUpMessages(type), cancellationToken);

    private async Task<int> RetryAsync(IQueryable<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        // Due now: a lane's last failure wrote a later try.
        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var retried = await messages.ExecuteUpdateAsync(
            set => set
                .SetProperty(message => message.ProcessedOnUtc, (DateTime?)null)
                .SetProperty(message => message.RetryCount, 0)
                .SetProperty(message => message.NextAttemptOnUtc, now),
            cancellationToken);
        if (retried > 0 && services.GetService<OutboxWakeUp<TDbContext>>() is { } wakeUp)
            await wakeUp.WakeAsync(cancellationToken);
        return retried;
    }

    private IQueryable<OutboxMessage> GivenUpMessages(string? type)
    {
        var givenUp = context.Set<OutboxMessage>().Where(message => message.ProcessedOnUtc == OutboxMessage.GivenUpProcessedOnUtc);
        return type is null ? givenUp : givenUp.Where(message => message.Type == type);
    }

    private Dictionary<string, string> LaneOfEachType()
    {
        var lanes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (services.GetService<OutboxLaneRegistry>() is not { } registry)
            return lanes;
        foreach (var (lane, eventTypes) in registry.Lanes)
            foreach (var eventType in eventTypes)
                lanes[eventType] = lane;
        return lanes;
    }
}
