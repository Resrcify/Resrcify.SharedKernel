namespace Resrcify.SharedKernel.Abstractions.Mediator;

/// <summary>
/// A request its caller can make idempotent: sent again with the same <see cref="IdempotencyKey"/>, it is answered with
/// the first one's result instead of being handled again (the mediator's idempotency behavior). Without a key it is
/// handled as usual.
/// </summary>
/// <remarks>
/// The key comes from whoever sends the request: an HTTP endpoint from its <c>Idempotency-Key</c> header, a consumer
/// from its message's ID. Keys are kept per request type and <see cref="IdempotencyScope"/>.
/// </remarks>
public interface IIdempotentRequest
{
    /// <summary>The caller's key for this request; <see langword="null"/> or empty: handled as usual.</summary>
    string? IdempotencyKey { get; }

    /// <summary>
    /// Whose keys these are, e.g. the user's ID, so one caller's key can't replay another's result;
    /// <see langword="null"/>: the key is shared by every caller of the request type.
    /// </summary>
    string? IdempotencyScope => null;
}
