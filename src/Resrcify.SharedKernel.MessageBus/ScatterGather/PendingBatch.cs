using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// One batch waiting for replies. Each item settles once; a later reply for it (delivery is at-least-once) is a
/// <see cref="ReplyOutcome.Duplicate"/> and is ignored.
/// </summary>
/// <param name="retainReplies">
/// Whether to keep every reply until the batch is gathered (<see cref="ToGathered{TResponse}"/>). A streamed batch
/// doesn't: a reply is only in <see cref="Arrivals"/> until read, and the batch remembers just which items settled.
/// </param>
internal sealed class PendingBatch(IEnumerable<string> keys, bool retainReplies = true)
{
    /// <summary>Stands in for a reply a streamed batch doesn't keep.</summary>
    private static readonly object Settled = new();

    private readonly HashSet<string> _keys = [.. keys];
    private readonly ConcurrentDictionary<string, object> _replies = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _allAnswered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Only a streamed batch has its replies read as they arrive; a gathered one is read once, from _replies.
    private readonly Channel<KeyValuePair<string, object>>? _arrivals = retainReplies
        ? null
        : Channel.CreateUnbounded<KeyValuePair<string, object>>(new UnboundedChannelOptions { SingleReader = true });

    // Counted here: ConcurrentDictionary.Count takes every one of its locks, on every reply.
    private int _answered;

    public Task AllAnswered => _allAnswered.Task;

    /// <summary>
    /// Each accepted reply, in the order they arrive; completed once every item has answered. Only a streamed batch
    /// has them: a gathered one keeps its replies for <see cref="ToGathered{TResponse}"/>.
    /// </summary>
    public ChannelReader<KeyValuePair<string, object>> Arrivals => _arrivals?.Reader
        ?? throw new InvalidOperationException("A gathered batch keeps its replies; it has no arrivals to stream.");

    public ReplyOutcome Record(string key, object message)
    {
        if (!_keys.Contains(key))
            return ReplyOutcome.Late;
        if (!_replies.TryAdd(key, retainReplies ? message : Settled))
            return ReplyOutcome.Duplicate;

        // Written before it is counted: the reply that completes the count finds every other reply already written.
        _arrivals?.Writer.TryWrite(new(key, message));
        if (Interlocked.Increment(ref _answered) == _keys.Count)
        {
            _allAnswered.TrySetResult();
            _arrivals?.Writer.TryComplete();
        }
        return ReplyOutcome.Accepted;
    }

    public Gathered<TResponse> ToGathered<TResponse>()
        where TResponse : class
        => !retainReplies
            ? throw new InvalidOperationException("A streamed batch keeps no replies to gather.")
            : new(
            _keys,
            _replies
                .Where(reply => reply.Value is TResponse)
                .ToDictionary(reply => reply.Key, reply => (TResponse)reply.Value, StringComparer.Ordinal),
            _replies
                .Where(reply => reply.Value is not TResponse)
                .ToDictionary(
                    reply => reply.Key,
                    reply => (IReadOnlyList<Error>)ErrorsOf(reply.Value),
                    StringComparer.Ordinal));

    /// <summary>One arrival as a reply: the response, or why there is none.</summary>
    public static IScatterReply<TResponse> ToReply<TResponse>(KeyValuePair<string, object> arrival)
        where TResponse : class
        => arrival.Value is TResponse response
            ? new ScatterReply<TResponse>(arrival.Key, response)
            : new ScatterReply<TResponse>(arrival.Key, ErrorsOf(arrival.Value));

    private static Error[] ErrorsOf(object reply)
        => reply is ScatterRequestFailed failed
            ? failed.ToErrors()
            : [ScatterGatherErrors.UnexpectedReply(reply.GetType().Name)];
}
