using System;
using Polly;
using Resrcify.SharedKernel.Abstractions.Resilience;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>
/// The Web package's <see cref="ITransientFailureRule"/>, registered by <c>AddResultResilience</c>: Polly refused or gave
/// up on a call (<see cref="ExecutionRejectedException"/>: a timeout, a broken or isolated circuit, the rate limiter),
/// which another try, later, may get past. Only a service using <c>AddResultResilience</c> references Polly, so only
/// such a service has this rule; the other packages never name Polly's types.
/// </summary>
internal sealed class PollyTransientFailureRule
    : ITransientFailureRule
{
    public bool IsTransient(Exception exception)
        => exception is ExecutionRejectedException;
}
