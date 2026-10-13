using System;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// How <see cref="IdempotencyPipelineBehavior{TRequest, TResponse}"/> keeps results. Set with <c>ConfigureIdempotency</c>
/// on the mediator's configuration (<c>AddMediator(cfg =&gt; cfg.ConfigureIdempotency(...))</c>).
/// </summary>
public sealed class IdempotencyPipelineOptions
{
    /// <summary>How long a result is kept, and given to a request repeating its key. 24 hours by default.</summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The longest a request holds its key while it is handled: a repeat meanwhile is answered with
    /// <see cref="IdempotencyErrors.InProgress"/>. One that runs longer (or whose instance died) lets a repeat through
    /// after this. 1 minute by default.
    /// </summary>
    public TimeSpan InProgressTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The longest key accepted. 255 by default.</summary>
    public int MaxKeyLength { get; set; } = 255;
}
