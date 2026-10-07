using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>
/// The messages of one kind the harness recorded (published, sent, consumed, faulted or dead-lettered), in the order it
/// saw them. Read what is there with <see cref="Of{TMessage}"/>, or wait for one with <see cref="WaitForAsync{TMessage}"/>.
/// </summary>
public sealed class MessageRecording
{
    /// <summary>How long <see cref="WaitForAsync{TMessage}"/> waits unless told otherwise.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly List<RecordedMessage> _messages = [];
    private readonly List<Waiter> _waiters = [];
    private readonly string _kind;

    internal MessageRecording(string kind)
        => _kind = kind;

    /// <summary>Everything recorded so far.</summary>
    public IReadOnlyList<RecordedMessage> All
    {
        get
        {
            lock (_gate)
                return [.. _messages];
        }
    }

    /// <summary>The messages recorded so far that are <typeparamref name="TMessage"/>s.</summary>
    public IReadOnlyList<TMessage> Of<TMessage>()
        where TMessage : class
    {
        lock (_gate)
            return [.. _messages.Select(recorded => recorded.Message).OfType<TMessage>()];
    }

    /// <summary>
    /// The first <typeparamref name="TMessage"/> recorded that <paramref name="match"/> accepts: at once if there is one
    /// already, or as soon as one is recorded. Throws a <see cref="TimeoutException"/> listing what was recorded when
    /// none is within <paramref name="timeout"/> (real time, whatever the service's clock).
    /// </summary>
    public async Task<TMessage> WaitForAsync<TMessage>(
        Func<TMessage, bool>? match = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        bool Accepts(object? message)
            => message is TMessage typed && (match?.Invoke(typed) ?? true);

        var waiter = new Waiter(Accepts);
        lock (_gate)
        {
            var found = _messages.Find(recorded => Accepts(recorded.Message));
            if (found is not null)
                return (TMessage)found.Message!;
            _waiters.Add(waiter);
        }

        var limit = timeout ?? DefaultTimeout;
        try
        {
            return (TMessage)await waiter.Found.Task.WaitAsync(limit, TimeProvider.System, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"No matching {typeof(TMessage).Name} was {_kind} within {limit.TotalSeconds} s. {Describe()}");
        }
        finally
        {
            lock (_gate)
                _waiters.Remove(waiter);
        }
    }

    internal void Add(RecordedMessage message)
    {
        List<Waiter> found;
        lock (_gate)
        {
            _messages.Add(message);
            found = _waiters.FindAll(waiter => waiter.Accepts(message.Message));
            foreach (var waiter in found)
                _waiters.Remove(waiter);
        }

        foreach (var waiter in found)
            waiter.Found.TrySetResult(message.Message!);
    }

    private string Describe()
    {
        var types = All
            .GroupBy(recorded => recorded.Message?.GetType().Name ?? recorded.MessageType ?? "unknown", StringComparer.Ordinal)
            .Select(group => $"{group.Key} x{group.Count()}")
            .ToList();
        return types.Count == 0
            ? $"Nothing was {_kind}."
            : $"{char.ToUpperInvariant(_kind[0])}{_kind[1..]}: {string.Join(", ", types)}.";
    }

    private sealed class Waiter(Func<object?, bool> accepts)
    {
        public Func<object?, bool> Accepts { get; } = accepts;

        public TaskCompletionSource<object> Found { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
