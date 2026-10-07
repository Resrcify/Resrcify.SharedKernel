using System;
using System.Collections.Generic;
using System.Globalization;
using Rebus.Messages;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// When a request stops being worth answering: its send time plus its time-to-live, both set by the sender
/// (a scatter request lives as long as its batch's timeout). After that its requester has stopped waiting.
/// </summary>
internal static class MessageDeadline
{
    /// <summary>
    /// How long is left to answer: until the deadline, but never more than the time-to-live itself (the sender's clock
    /// and this one can disagree, e.g. a test's fake clock years behind; the request can't have more time than it was
    /// sent with). <see langword="null"/> without the headers.
    /// </summary>
    public static TimeSpan? Remaining(IReadOnlyDictionary<string, string> headers, DateTimeOffset now)
    {
        if (!TryRead(headers, out var deadline, out var timeToLive))
            return null;
        var remaining = deadline - now;
        return remaining < timeToLive ? remaining : timeToLive;
    }

    public static bool TryRead(IReadOnlyDictionary<string, string> headers, out DateTimeOffset deadline)
        => TryRead(headers, out deadline, out _);

    private static bool TryRead(IReadOnlyDictionary<string, string> headers, out DateTimeOffset deadline, out TimeSpan timeToLive)
    {
        ArgumentNullException.ThrowIfNull(headers);
        deadline = default;
        timeToLive = default;
        if (!headers.TryGetValue(Headers.SentTime, out var sentText) ||
            !headers.TryGetValue(Headers.TimeToBeReceived, out var timeToLiveText))
            return false;
        if (!DateTimeOffset.TryParse(sentText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sent) ||
            !TimeSpan.TryParse(timeToLiveText, CultureInfo.InvariantCulture, out timeToLive))
            return false;
        deadline = sent + timeToLive;
        return true;
    }
}
