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
    public static bool TryRead(IReadOnlyDictionary<string, string> headers, out DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(headers);
        deadline = default;
        if (!headers.TryGetValue(Headers.SentTime, out var sentText) ||
            !headers.TryGetValue(Headers.TimeToBeReceived, out var timeToLiveText))
            return false;
        if (!DateTimeOffset.TryParse(sentText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sent) ||
            !TimeSpan.TryParse(timeToLiveText, CultureInfo.InvariantCulture, out var timeToLive))
            return false;
        deadline = sent + timeToLive;
        return true;
    }
}
