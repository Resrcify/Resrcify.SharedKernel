using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>The failures of <see cref="IdempotencyPipelineBehavior{TRequest, TResponse}"/>.</summary>
public static class IdempotencyErrors
{
    /// <summary>The key is blank or too long (HTTP 400).</summary>
    public static Error InvalidKey(int maxLength)
        => Error.Validation(ErrorCodes.Idempotency.InvalidKey, $"An idempotency key is 1 to {maxLength} characters, not blank.");

    /// <summary>The key was already used for a different request (HTTP 422).</summary>
    public static Error KeyReused
        => Error.Unprocessable(ErrorCodes.Idempotency.KeyReused, "The idempotency key was already used for a different request.");

    /// <summary>A request with the key is still being handled (HTTP 409): try again shortly.</summary>
    public static Error InProgress
        => Error.Conflict(ErrorCodes.Idempotency.InProgress, "A request with this idempotency key is still being handled.");
}
