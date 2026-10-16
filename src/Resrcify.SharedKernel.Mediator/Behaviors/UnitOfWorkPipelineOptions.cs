namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// How <see cref="UnitOfWorkPipelineBehavior{TRequest, TResponse}"/> saves. Set with <c>ConfigureUnitOfWork</c> on the
/// mediator's configuration (<c>AddMediator(cfg =&gt; cfg.ConfigureUnitOfWork(...))</c>).
/// </summary>
public sealed class UnitOfWorkPipelineOptions
{
    /// <summary>
    /// Save with <c>IUnitOfWork.TryCompleteAsync</c> and return its failure as the command's result, instead of letting
    /// the exception through: a concurrency conflict, a unique-constraint or a foreign-key violation becomes a
    /// <c>Conflict</c> (409), a not-null or check-constraint violation or a string too long for its column a
    /// <c>Validation</c> error (400), a serialization failure or a deadlock outside a transaction a transient
    /// <c>Failure</c>. Anything else still throws.
    /// <see langword="false"/> by default (the save throws, as before).
    /// </summary>
    /// <remarks>
    /// Inside a transaction (a transactional command), a serialization failure or a deadlock still throws: it aborted
    /// the whole transaction, so the transaction behavior's execution strategy (with <c>RetryOnFailure</c>) runs the
    /// command again, and only the transaction's owner can.
    /// </remarks>
    public bool ReturnPersistenceFailures { get; set; }
}
