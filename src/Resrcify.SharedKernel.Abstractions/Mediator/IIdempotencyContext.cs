namespace Resrcify.SharedKernel.Abstractions.Mediator;

/// <summary>
/// Which requests sent in this scope were answered from an earlier result (an <see cref="IIdempotentRequest"/> sent
/// again with its key) instead of being handled: an HTTP endpoint marks its response with it.
/// </summary>
public interface IIdempotencyContext
{
    /// <summary>Whether <paramref name="request"/> (this instance) was answered with an earlier result.</summary>
    bool WasReplayed(object request);
}
