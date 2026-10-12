using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// The service's error queue (<c>&lt;input queue&gt;.error</c>): the integration events whose handlers kept failing.
/// Shows them, deletes them, or moves them back to the queue they failed on, to be handled again.
/// </summary>
/// <remarks>
/// <para>
/// A message is named by its message ID (<see cref="FailedMessage.MessageId"/>). An operation on one ID takes every
/// message with it (an event published again by a retried outbox message, that failed again, has the same ID).
/// </para>
/// <para>
/// A message moved back keeps its message ID and loses its error details. Its handlers that had already handled it
/// (or rejected it as the event's fault) before the one that failed are not run again; the one that failed and those
/// after it are. A service that skips duplicates (<c>SkipDuplicateEvents</c>) handles it again: the claim was released
/// when it went to the error queue.
/// </para>
/// <para>
/// Each operation goes through the queue once, holding the messages it reads until it is done, so messages that arrive
/// meanwhile are left for the next one. Fails with <c>ErrorQueue.None</c> when the service has no input queue (it
/// subscribes to nothing, so nothing goes to an error queue), and with <c>ErrorQueue.Unavailable</c> when the broker
/// can't be reached.
/// </para>
/// </remarks>
public interface IErrorQueue
{
    /// <summary>The first <paramref name="max"/> messages, oldest first, and how many the queue holds.</summary>
    Task<Result<ErrorQueueContents>> ListAsync(
        int max = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the message(s) with <paramref name="messageId"/>; <c>ErrorQueue.MessageNotFound</c> if none.</summary>
    Task<Result> DeleteAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every message, or every message of <paramref name="messageType"/> (its wire name, e.g.
    /// <c>ShardRankChanged</c>). Answers how many were deleted.
    /// </summary>
    Task<Result<int>> DeleteAllAsync(
        string? messageType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the message(s) with <paramref name="messageId"/> back to the queue they failed on;
    /// <c>ErrorQueue.MessageNotFound</c> if none, <c>ErrorQueue.NotMoved</c> if one couldn't be (it stays).
    /// </summary>
    Task<Result> RetryAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves every message, or every message of <paramref name="messageType"/> (its wire name), back to the queue it
    /// failed on. Answers how many were moved; one that couldn't be stays, and is logged.
    /// </summary>
    Task<Result<int>> RetryAllAsync(
        string? messageType = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What an error queue holds: its name, how many messages, and the first of them.</summary>
public sealed record ErrorQueueContents(
    string Queue,
    int Count,
    IReadOnlyList<FailedMessage> Messages);

/// <summary>
/// A message in the error queue: its ID, its wire name (<see cref="MessageType"/>), the queue it failed on, why it
/// failed (<see cref="Error"/>: the handler and its errors, or the exception), when it was sent, its headers, and its
/// body (unzipped, if it was gzipped). <see cref="Text"/> is the body as text when it is JSON or text.
/// </summary>
public sealed record FailedMessage(
    string MessageId,
    string? MessageType,
    string? SourceQueue,
    string? Error,
    DateTimeOffset? SentOnUtc,
    IReadOnlyDictionary<string, string> Headers,
    string? Text,
    ReadOnlyMemory<byte> Body);
