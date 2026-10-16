using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Abstractions.UnitOfWork;

/// <summary>
/// Looks into <typeparamref name="TContext"/>'s outbox and acts on the messages that gave up (poison): what an operator
/// needs instead of hand-written SQL. The unit of work package implements it for each DbContext with an outbox
/// (registered, scoped, with the outbox: <c>AddOutboxProcessing</c>, <c>AddProcessOutboxMessagesJob</c>,
/// <c>AddOutboxLanes</c>); the Web package maps it to admin endpoints (<c>MapOutboxAdministration&lt;TContext&gt;</c>).
/// </summary>
/// <typeparam name="TContext">The DbContext whose outbox this is.</typeparam>
/// <remarks>
/// A message that gave up stays in the outbox, marked, with its last error. Only those can be tried again or deleted
/// here: a message still waiting is the outbox's own, and a processed one is cleaned up by it.
/// </remarks>
public interface IOutboxAdministration<TContext>
    where TContext : class
{
    /// <summary>The most messages <see cref="ListGivenUpAsync"/> returns at once.</summary>
    const int MaxTake = 500;

    /// <summary>The DbContext whose outbox this is (<typeparamref name="TContext"/>).</summary>
    Type ContextType => typeof(TContext);

    /// <summary>Counts of the messages waiting, retrying (waiting after a failed try) and given up, per event type.</summary>
    Task<OutboxSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The messages that gave up, the most recent events first; only <paramref name="type"/>'s when given.
    /// <paramref name="take"/> is 1 to <see cref="MaxTake"/>.
    /// </summary>
    Task<IReadOnlyList<OutboxMessageSummary>> ListGivenUpAsync(
        int take = 50,
        string? type = null,
        CancellationToken cancellationToken = default);

    /// <summary>A message with its content; <see langword="null"/> when there is none with <paramref name="id"/> (or it was cleaned up).</summary>
    Task<OutboxMessageDetails?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries a message that gave up again, from its first try (its last error is kept until the next one), and wakes the
    /// outbox. <see langword="false"/> when no message with <paramref name="id"/> gave up.
    /// </summary>
    Task<bool> RetryAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Tries every message that gave up again (only <paramref name="type"/>'s when given); how many.</summary>
    Task<int> RetryAllGivenUpAsync(
        string? type = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message that gave up, for good: its event is never handled. <see langword="false"/> when no message with
    /// <paramref name="id"/> gave up.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every message that gave up (only <paramref name="type"/>'s when given), for good; how many.</summary>
    Task<int> DeleteAllGivenUpAsync(
        string? type = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The outbox now: messages waiting (and retrying) and messages that gave up, in all and per event type.</summary>
/// <param name="OldestWaitingOccurredOnUtc">When the oldest waiting message's event occurred; <see langword="null"/> when none waits.</param>
public sealed record OutboxSummary(
    int Waiting,
    int Retrying,
    int GivenUp,
    DateTime? OldestWaitingOccurredOnUtc,
    IReadOnlyList<OutboxEventTypeSummary> EventTypes);

/// <summary>One event type's messages; <paramref name="Lane"/> is its outbox lane, <see langword="null"/> for the regular job.</summary>
public sealed record OutboxEventTypeSummary(
    string Type,
    string? Lane,
    int Waiting,
    int Retrying,
    int GivenUp);

/// <summary>A message that gave up, without its content.</summary>
public sealed record OutboxMessageSummary(
    Guid Id,
    string Type,
    DateTime OccurredOnUtc,
    int RetryCount,
    string? Error);

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
