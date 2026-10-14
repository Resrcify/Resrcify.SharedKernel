using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>
/// The failures <c>AddResultResilience</c> answers in the service's place, unless
/// <see cref="ResultResilienceOptions.ThrowTimeoutsAndOpenCircuits"/>: when the call timed out or the circuit is open,
/// the caller gets a response made up by the pipeline, problem details as SharedKernel writes them, holding one of these
/// errors and marked with the <see cref="Header"/> header. <c>ToResultAsync</c> reads it as a failure with that error,
/// like any other failed answer; <c>EnsureSuccessStatusCode</c> throws an <c>HttpRequestException</c> for it.
/// </summary>
public static class ResultResilienceErrors
{
    /// <summary>
    /// The header on a response the pipeline made up: <see cref="TimeoutMarker"/> or <see cref="CircuitOpenMarker"/>.
    /// A response from the service called never has it.
    /// </summary>
    public const string Header = "Resrcify-Resilience";

    /// <summary><see cref="Header"/>'s value on <see cref="Timeout"/>'s response.</summary>
    public const string TimeoutMarker = "timeout";

    /// <summary><see cref="Header"/>'s value on <see cref="CircuitOpen"/>'s response.</summary>
    public const string CircuitOpenMarker = "circuit-open";

    /// <summary>
    /// An attempt (after the retries) or the whole call took longer than the pipeline allows. Answered as a 504
    /// (<see cref="ErrorType.Timeout"/>).
    /// </summary>
    public static readonly Error Timeout = Error.Timeout(
        "Http.ResilienceTimeout",
        "The call timed out: an attempt or the whole call took longer than the resilience pipeline allows.");

    /// <summary>
    /// The circuit is open after too many transient failures, so the call wasn't made. Answered as a 502
    /// (<see cref="ErrorType.ExternalFailure"/>, transient).
    /// </summary>
    public static readonly Error CircuitOpen = Error.ExternalFailure(
        "Http.CircuitOpen",
        "The call wasn't made: the circuit is open after too many transient failures of the service called.");
}
