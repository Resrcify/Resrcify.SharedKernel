using System;
using System.Collections.Concurrent;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// The events this instance is handling right now, by the key that marks them handled (with
/// <c>SkipDuplicateEvents</c>). A copy of an event that arrives while the event is being handled here is a duplicate
/// without asking the cache, so two copies can't both be handled by one instance, however the cache claims keys.
/// </summary>
internal sealed class EventsInFlight
{
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

    /// <summary><see langword="true"/> when nobody here handles <paramref name="key"/>: the caller does, until <see cref="Finish"/>.</summary>
    public bool TryStart(string key)
        => _keys.TryAdd(key, 0);

    public void Finish(string key)
        => _keys.TryRemove(key, out _);
}
