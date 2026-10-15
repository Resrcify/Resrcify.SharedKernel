using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>
/// The failures <c>AddResultResilience</c> answers in the service's place, unless
/// <see cref="ResultResilienceOptions.ThrowTimeoutsAndOpenCircuits"/>: when the call timed out, the circuit is open or
/// the service couldn't be reached after the retries, the caller gets a response made up by the pipeline, problem
/// details as SharedKernel writes them, holding one of these errors and marked with the <see cref="Header"/> header.
/// <c>ToResultAsync</c> reads it as a failure with that error, like any other failed answer;
/// <c>EnsureSuccessStatusCode</c> throws an <c>HttpRequestException</c> for it.
/// </summary>
public static class ResultResilienceErrors
{
    /// <summary>
    /// The header on a response the pipeline made up: <see cref="TimeoutMarker"/>, <see cref="CircuitOpenMarker"/> or
    /// <see cref="UnreachableMarker"/>. A response from the service called never has it.
    /// </summary>
    public const string Header = "Resrcify-Resilience";

    /// <summary><see cref="Header"/>'s value on <see cref="Timeout"/>'s response.</summary>
    public const string TimeoutMarker = "timeout";

    /// <summary><see cref="Header"/>'s value on <see cref="CircuitOpen"/>'s response.</summary>
    public const string CircuitOpenMarker = "circuit-open";

    /// <summary><see cref="Header"/>'s value on <see cref="Unreachable"/>'s response.</summary>
    public const string UnreachableMarker = "unreachable";

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

    /// <summary>
    /// The service at <paramref name="authority"/> couldn't be reached, after the retries: HttpClient got no answer (a
    /// refused or reset connection, a name that doesn't resolve, a TLS failure: an <c>HttpRequestException</c> without a
    /// status). Answered as a 502 (<see cref="ErrorType.ExternalFailure"/>, transient), coded <c>Http.Unreachable</c>.
    /// </summary>
    /// <param name="authority">The host (and port) called, e.g. <c>swgohapi:8080</c>.</param>
    /// <param name="cause">Why: the exception's message, e.g. <c>Connection refused (swgohapi:8080)</c>.</param>
    public static Error Unreachable(
        string authority,
        string cause)
        => Error.ExternalFailure(
            "Http.Unreachable",
            $"The service at {authority} could not be reached: {cause}");
}
