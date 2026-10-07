using System;

namespace Resrcify.SharedKernel.Results.Primitives;

public static class ErrorTypeExtensions
{
    /// <summary>
    /// The code of a concurrency conflict (the data changed since it was read), e.g. UnitOfWork's
    /// <c>PersistenceErrors.Concurrency</c>: a <see cref="ErrorType.Conflict"/> to a caller (an HTTP 409, "read it again"),
    /// but transient to whoever can try again, since another try reads the data again (see <see cref="IsTransient(Error)"/>).
    /// </summary>
    public const string ConcurrencyConflictCode = "Persistence.Concurrency";

    /// <summary>
    /// Whether an error of this type may pass on another try (<see cref="ErrorType.Failure"/>,
    /// <see cref="ErrorType.ExternalFailure"/>, <see cref="ErrorType.Timeout"/>, <see cref="ErrorType.RateLimit"/>).
    /// The others (<see cref="ErrorType.NotFound"/>, <see cref="ErrorType.Validation"/>,
    /// <see cref="ErrorType.Conflict"/>, <see cref="ErrorType.Unauthorized"/>, <see cref="ErrorType.Forbidden"/>) are
    /// about the request itself: another try with the same request fails the same way.
    /// </summary>
    /// <remarks>
    /// The message bus retries by it, the mediator's logging picks its level by it, and a requester uses it to tell a
    /// definite answer (e.g. "no such player") from a responder that kept failing until its last try (e.g. its
    /// upstream was down). A plain <see cref="ErrorType.Failure"/> counts as transient: it may be a bug, but retrying
    /// it is the safe default.
    /// </remarks>
    public static bool IsTransient(this ErrorType type)
        => type
            is ErrorType.Failure
            or ErrorType.ExternalFailure
            or ErrorType.Timeout
            or ErrorType.RateLimit;

    /// <summary>
    /// Whether another try may pass: the error's type is transient (<see cref="IsTransient(ErrorType)"/>), or it is a
    /// concurrency conflict (<see cref="ConcurrencyConflictCode"/>), which a try in a new scope reads again and may pass.
    /// The message bus retries by it, and logging picks its level by it.
    /// </summary>
    public static bool IsTransient(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Type.IsTransient() || error.Code == ConcurrencyConflictCode;
    }
}
