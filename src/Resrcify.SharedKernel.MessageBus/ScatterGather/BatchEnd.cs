namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>How a scatter-gather batch ended.</summary>
internal enum BatchEnd
{
    /// <summary>Every item answered or failed.</summary>
    Complete,

    /// <summary>The timeout passed with items still unanswered.</summary>
    Timeout,

    /// <summary>A streaming caller stopped reading, or was cancelled, before the batch ended.</summary>
    Stopped,
}
