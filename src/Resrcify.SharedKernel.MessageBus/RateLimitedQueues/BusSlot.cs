using System;
using System.Threading;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// The bus a rate-limited queue consumes on, if any: put by its host's loop when it starts one, taken out by whoever
/// stops it (the loop stepping aside or restarting, the host's <c>StopAsync</c> and <c>Dispose</c>, which a test host
/// may run at the same time). Each bus is taken out once, so it is stopped once, and once the slot is closed (the host
/// stopped) no bus stays in it: one put while it closes is taken back by the one putting it.
/// </summary>
/// <remarks>
/// Every read and write is an interlocked operation, so in any interleaving of <see cref="TryPut"/> with
/// <see cref="Close"/>, the bus ends up with exactly one of them: the close takes it out, or the put sees the slot
/// closed and takes it back.
/// </remarks>
internal sealed class BusSlot<TBus>
    where TBus : class
{
    private TBus? _bus;
    private int _closed;

    /// <summary>Whether the slot holds a bus (the queue is consuming).</summary>
    public bool HasBus => Volatile.Read(ref _bus) is not null;

    /// <summary>Whether the host stopped: no bus is to start any more.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    /// <summary>
    /// Puts a started bus in the slot. <see langword="false"/> when the slot is closed (or holds a bus already): the
    /// caller still has <paramref name="bus"/> and stops it. <see langword="true"/> when it is in the slot, from where
    /// whoever takes it out stops it.
    /// </summary>
    public bool TryPut(TBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        if (IsClosed)
            return false;
        if (Interlocked.CompareExchange(ref _bus, bus, null) is not null)
            return false;
        if (!IsClosed)
            return true;

        // Closed while it was being put: whoever takes it out (the close, or this) stops it.
        return !ReferenceEquals(Interlocked.CompareExchange(ref _bus, null, bus), bus);
    }

    /// <summary>Takes the bus out, for the caller to stop; <see langword="null"/> when there is none (someone else took it).</summary>
    public TBus? Take()
        => Interlocked.Exchange(ref _bus, null);

    /// <summary>Closes the slot, so no bus is put in it any more, and takes out the one there is, for the caller to stop.</summary>
    public TBus? Close()
    {
        Interlocked.Exchange(ref _closed, 1);
        return Take();
    }
}
