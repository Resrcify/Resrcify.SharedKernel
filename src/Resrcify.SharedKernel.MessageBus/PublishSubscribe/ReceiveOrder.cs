using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Pipeline;
using Rebus.Transport;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// The order the service's bus received its messages in, so events handled in publish order take their place in their
/// partition in that order, though up to <c>MaxParallelism</c> messages are handled at once.
/// </summary>
/// <remarks>
/// <para>
/// Each received message gets the next number (<see cref="Stamp"/>). A message is <em>admitted</em> once it has taken
/// its place: an ordered event when it has joined its partition, any other message as soon as it is read. An ordered
/// event waits until every message before it has been admitted (<see cref="WaitForTurnAsync"/>).
/// </para>
/// <para>
/// Every message is admitted at the latest when its handling ends (the transaction context's <c>OnDisposed</c>), so a
/// message that fails before it is read can't hold the others up; and a wait gives up after
/// <see cref="LongestWait"/>, so a bug can delay events but never stop them.
/// </para>
/// </remarks>
internal sealed class ReceiveOrder(TimeProvider? timeProvider = null)
{
    /// <summary>The transaction context item that holds a received message's number.</summary>
    public const string ItemKey = "resrcify-receive-order";

    public static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly HashSet<long> _admittedAhead = [];
    private readonly Dictionary<long, TaskCompletionSource> _waiting = [];
    private long _lastStamped;
    private long _nextToAdmit = 1;

    /// <summary>Numbers a message just received, and admits it when its handling ends whatever happens to it.</summary>
    public long Stamp(ITransactionContext context)
    {
        var number = Interlocked.Increment(ref _lastStamped);
        context.Items[ItemKey] = number;
        context.OnDisposed(_ => Admit(number));
        return number;
    }

    /// <summary>The number of the message being handled, if its bus numbers messages.</summary>
    public static long? NumberOf(IMessageContext? context)
        => context?.TransactionContext.Items.TryGetValue(ItemKey, out var number) == true && number is long value
            ? value
            : null;

    /// <summary>Completes when every message received before <paramref name="number"/> has been admitted.</summary>
    public async Task WaitForTurnAsync(long number, CancellationToken cancellationToken)
    {
        TaskCompletionSource turn;
        lock (_gate)
        {
            if (_nextToAdmit >= number)
                return;
            turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting[number] = turn;
        }

        try
        {
            await turn.Task.WaitAsync(LongestWait, timeProvider ?? TimeProvider.System, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Something before it was never admitted: go on rather than hold the queue up.
        }
        finally
        {
            lock (_gate)
                _waiting.Remove(number);
        }
    }

    /// <summary>Marks <paramref name="number"/> as having taken its place; calling it again does nothing.</summary>
    public void Admit(long number)
    {
        TaskCompletionSource? next;
        lock (_gate)
        {
            if (number < _nextToAdmit || !_admittedAhead.Add(number))
                return;
            while (_admittedAhead.Remove(_nextToAdmit))
                _nextToAdmit++;
            _waiting.TryGetValue(_nextToAdmit, out next);
        }
        next?.TrySetResult();
    }
}
