using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>The errors scatter-gather itself reports.</summary>
public static class ScatterGatherErrors
{
    /// <summary>The item didn't answer before the timeout.</summary>
    public static Error Unanswered(string key)
        => new("ScatterGather.Unanswered", $"Item {key} didn't answer before the timeout.", ErrorType.Timeout);

    /// <summary>The item answered with a message of a type nobody expected.</summary>
    public static Error UnexpectedReply(string type)
        => new("ScatterGather.UnexpectedReply", $"Unexpected reply {type}.", ErrorType.Failure);
}
