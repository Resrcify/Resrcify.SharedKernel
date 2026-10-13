namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>How handling an integration event ended.</summary>
internal enum EventOutcome
{
    Success,

    /// <summary>A handler kept failing (or throwing) through its tries in place: moved to the service's error queue.</summary>
    Error,

    /// <summary>A handler failed in a way that is the event's fault: logged, not retried.</summary>
    Rejected,

    /// <summary>This service had already handled the same message (it skips duplicates): not handled again.</summary>
    Duplicate,
}
