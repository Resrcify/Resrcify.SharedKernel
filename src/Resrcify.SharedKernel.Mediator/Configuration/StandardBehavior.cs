namespace Resrcify.SharedKernel.Mediator.Configuration;

/// <summary>
/// The behaviors of the standard set (<see cref="MediatorConfiguration.AddStandardBehaviors"/>), in their order: the
/// first is the outermost.
/// </summary>
public enum StandardBehavior
{
    /// <summary><c>LoggingPipelineBehavior</c>: logs every request, its failure or exception, and its time.</summary>
    Logging,

    /// <summary><c>ValidationPipelineBehavior</c>: runs the request's validators; an invalid request fails here.</summary>
    Validation,

    /// <summary><c>TransactionPipelineBehavior</c>: runs an <c>ITransactionalCommand</c> in a transaction.</summary>
    Transaction,

    /// <summary><c>UnitOfWorkPipelineBehavior</c>: saves a command's changes when it succeeds.</summary>
    UnitOfWork,

    /// <summary><c>CachingPipelineBehavior</c>: answers an <c>ICachingQuery</c> from the cache, and caches its result.</summary>
    Caching,
}
