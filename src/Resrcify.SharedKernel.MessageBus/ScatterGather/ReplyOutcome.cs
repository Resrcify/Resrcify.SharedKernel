namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>What became of one scatter-gather reply.</summary>
internal enum ReplyOutcome
{
    /// <summary>The item's answer.</summary>
    Accepted,

    /// <summary>The item was already answered; ignored.</summary>
    Duplicate,

    /// <summary>The batch was already gathered (or the item isn't in it); ignored.</summary>
    Late,

    /// <summary>The reply couldn't be read (e.g. a type this instance doesn't receive); dropped.</summary>
    Unreadable,
}
