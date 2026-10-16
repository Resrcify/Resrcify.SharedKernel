using System.Globalization;

namespace Resrcify.SharedKernel.Results.Primitives;

/// <summary>
/// The codes of every error SharedKernel itself returns or answers with, one constant per code, so a service matches
/// on <see cref="Error.Code"/> without repeating a string (e.g. <c>error.Code == ErrorCodes.Http.Unreachable</c>).
/// Grouped by the package that emits them.
/// </summary>
public static class ErrorCodes
{
    /// <summary>The codes of the errors <see cref="Error"/> and <see cref="Result"/> themselves use.</summary>
    public static class General
    {
        /// <summary><see cref="Error.NullValue"/>: a result's value was <see langword="null"/>.</summary>
        public const string NullValue = "Error.NullValue";

        /// <summary><see cref="Error.EmptyInput"/>: an input collection was empty.</summary>
        public const string EmptyInput = "Error.EmptyInput";
    }

    /// <summary>
    /// The codes of the errors the Web package reads a response back as (<c>ToResultAsync</c>), or answers with in a
    /// service's place (<c>AddResultResilience</c>).
    /// </summary>
    public static class Http
    {
        /// <summary>An attempt (after the retries) or the whole call took longer than the resilience pipeline allows.</summary>
        public const string ResilienceTimeout = "Http.ResilienceTimeout";

        /// <summary>The call wasn't made: the circuit is open.</summary>
        public const string CircuitOpen = "Http.CircuitOpen";

        /// <summary>The service called couldn't be reached after the retries (no answer at all).</summary>
        public const string Unreachable = "Http.Unreachable";

        /// <summary>A successful response had no value in it.</summary>
        public const string EmptyContent = "Http.EmptyContent";

        /// <summary>A successful response's body couldn't be read as the type asked for.</summary>
        public const string UnreadableContent = "Http.UnreadableContent";

        /// <summary>
        /// The code of a failed response without errors of its own (no problem details, or none with errors):
        /// <c>Http.&lt;status&gt;</c>, e.g. <c>Http.503</c>.
        /// </summary>
        /// <param name="statusCode">The response's status.</param>
        public static string ForStatus(int statusCode)
            => "Http." + statusCode.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The codes of UnitOfWork's <c>PersistenceErrors</c>: a save the database refused.</summary>
    public static class Persistence
    {
        /// <summary>
        /// A row changed (or was deleted) since it was read: a <see cref="ErrorType.Conflict"/> to a caller, but transient
        /// to whoever can try again in a new scope (<see cref="ErrorTypeExtensions.IsTransient(Error)"/>).
        /// </summary>
        public const string Concurrency = "Persistence.Concurrency";

        /// <summary>A value that must be unique is duplicated.</summary>
        public const string UniqueViolation = "Persistence.UniqueViolation";

        /// <summary>A value the database requires is missing.</summary>
        public const string NotNullViolation = "Persistence.NotNullViolation";

        /// <summary>A value breaks a check constraint.</summary>
        public const string CheckViolation = "Persistence.CheckViolation";

        /// <summary>A string is longer than its column allows.</summary>
        public const string ValueTooLong = "Persistence.ValueTooLong";

        /// <summary>A foreign key refers to a row that doesn't exist, or a row still referred to is deleted.</summary>
        public const string ForeignKeyViolation = "Persistence.ForeignKeyViolation";

        /// <summary>The save collided with a concurrent one (a serialization failure).</summary>
        public const string SerializationFailure = "Persistence.SerializationFailure";

        /// <summary>The save deadlocked with a concurrent one.</summary>
        public const string Deadlock = "Persistence.Deadlock";
    }

    /// <summary>The codes the Web package's exception handler answers a request with.</summary>
    public static class Request
    {
        /// <summary>An exception nothing handled: a 500.</summary>
        public const string Unhandled = "Unhandled";

        /// <summary>
        /// A request that couldn't be bound, when the exception names no parameter (otherwise the code is the
        /// parameter's name).
        /// </summary>
        public const string Binding = "Request";
    }

    /// <summary>The codes of the mediator's idempotency behavior.</summary>
    public static class Idempotency
    {
        /// <summary>The idempotency key is blank or too long.</summary>
        public const string InvalidKey = "Idempotency.InvalidKey";

        /// <summary>The key was already used for a different request.</summary>
        public const string KeyReused = "Idempotency.KeyReused";

        /// <summary>A request with the key is still being handled.</summary>
        public const string InProgress = "Idempotency.InProgress";
    }

    /// <summary>The codes scatter-gather itself reports.</summary>
    public static class ScatterGather
    {
        /// <summary>An item didn't answer before the timeout.</summary>
        public const string Unanswered = "ScatterGather.Unanswered";

        /// <summary>An item answered with a message of a type nobody expected.</summary>
        public const string UnexpectedReply = "ScatterGather.UnexpectedReply";

        /// <summary>
        /// A rate-limited queue's responder threw (not a <c>TransientFailureException</c>):
        /// <c>&lt;request type&gt;.ResponderFailed</c>, e.g. <c>PlayerQuestion.ResponderFailed</c>.
        /// </summary>
        /// <param name="requestType">The request's type name.</param>
        public static string ResponderFailed(string requestType)
            => requestType + ".ResponderFailed";
    }

    /// <summary>The codes of the message bus' error queue administration.</summary>
    public static class ErrorQueue
    {
        /// <summary>The service has no error queue (no input queue).</summary>
        public const string None = "ErrorQueue.None";

        /// <summary>No message with that id is in the error queue.</summary>
        public const string MessageNotFound = "ErrorQueue.MessageNotFound";

        /// <summary>A message couldn't be moved back, and stays in the error queue.</summary>
        public const string NotMoved = "ErrorQueue.NotMoved";

        /// <summary>The broker holding the error queue couldn't be reached.</summary>
        public const string Unavailable = "ErrorQueue.Unavailable";
    }

    /// <summary>The codes of the Web package's claims helpers.</summary>
    public static class User
    {
        /// <summary>The caller's user id claim is missing or isn't a <see cref="System.Guid"/>.</summary>
        public const string MissingUserIdClaim = "User.MissingUserIdClaim";
    }
}
