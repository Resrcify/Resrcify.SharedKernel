using System;
using System.Collections.Generic;
using System.Globalization;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>Which try of a request this is, carried in a header when it is sent back to its queue.</summary>
internal static class RequestTries
{
    public const string Header = "resrcify-try";

    /// <summary>The try this delivery is: 1 for the first, from the header after that.</summary>
    public static int Read(IReadOnlyDictionary<string, string> headers)
        => headers.TryGetValue(Header, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempt)
            && attempt > 0
            ? attempt
            : 1;

    /// <summary>The header for the next try.</summary>
    public static Dictionary<string, string> Next(int attempt)
        => new(StringComparer.Ordinal) { [Header] = (attempt + 1).ToString(CultureInfo.InvariantCulture) };
}
