using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>What a failed result says about the message it was for: whether another try could pass.</summary>
internal static class MessageFailures
{
    /// <summary>
    /// Every error is about the message itself (none <see cref="ErrorTypeExtensions.IsTransient"/>): another try would
    /// fail the same way. Anything else may pass next time.
    /// </summary>
    public static bool AreTheMessagesFault(IReadOnlyList<Error> errors)
        => errors.Count > 0 && !errors.Any(error => error.Type.IsTransient());
}
