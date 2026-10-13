using System;
using System.Collections.Generic;
using Rebus.Messages;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>A message one of the service's buses published, sent, consumed or failed, as the harness saw it.</summary>
/// <param name="Message">
/// The message: the publisher's object for a sent or published one, the receiver's (its own class for the wire name) for
/// one consumed or failed. <see langword="null"/> for a dead-lettered message that never reached its handler (unreadable).
/// </param>
/// <param name="Headers">Its headers (a copy).</param>
/// <param name="Destinations">The queues it was sent to (sent or published); empty for one received.</param>
/// <param name="Queue">The queue it was received from (consumed, faulted or dead-lettered); <see langword="null"/> for one sent.</param>
/// <param name="Exception">Why it failed (faulted or dead-lettered).</param>
public sealed record RecordedMessage(
    object? Message,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<string> Destinations,
    string? Queue,
    Exception? Exception)
{
    /// <summary>The message's type as Rebus wrote it on the wire.</summary>
    public string? MessageType
        => Headers.TryGetValue(Rebus.Messages.Headers.Type, out var type) ? type : null;

    /// <summary>The message's ID.</summary>
    public string? MessageId
        => Headers.TryGetValue(Rebus.Messages.Headers.MessageId, out var id) ? id : null;

    internal static RecordedMessage Received(Message message, string? queue, Exception? exception = null)
        => new(message.Body, Copy(message.Headers), [], queue, exception);

    internal static RecordedMessage Outgoing(Message message, IReadOnlyList<string> destinations)
        => new(message.Body, Copy(message.Headers), destinations, null, null);

    private static Dictionary<string, string> Copy(Dictionary<string, string> headers)
        => new(headers, StringComparer.Ordinal);
}
