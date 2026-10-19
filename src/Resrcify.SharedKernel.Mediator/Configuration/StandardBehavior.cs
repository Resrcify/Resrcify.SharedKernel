namespace Resrcify.SharedKernel.Mediator.Configuration;

/// <summary>
/// The behaviors of the standard set (<see cref="MediatorConfiguration.AddStandardBehaviors"/>). They run in this order,
/// the first the outermost: <see cref="Logging"/> → <see cref="Idempotency"/> → <see cref="Validation"/> →
/// <see cref="NonConcurrent"/> → <see cref="Transaction"/> → <see cref="UnitOfWork"/> → <see cref="Caching"/>.
/// </summary>
/// <remarks>
/// The members are not declared in that order (<see cref="Idempotency"/> came in 4.0 and <see cref="NonConcurrent"/>
/// in 4.6, each after the others, and keep their values): use the names, never the values, to order behaviors.
/// </remarks>
public enum StandardBehavior
{
    /// <summary>
    /// <c>LoggingPipelineBehavior</c>: logs every request, its failure or exception, and its time. The outermost.
    /// </summary>
    Logging,

    /// <summary>
    /// <c>ValidationPipelineBehavior</c>: runs the request's validators; an invalid request fails here. Runs after
    /// <see cref="Idempotency"/>.
    /// </summary>
    Validation,

    /// <summary><c>TransactionPipelineBehavior</c>: runs an <c>ITransactionalCommand</c> in a transaction.</summary>
    Transaction,

    /// <summary><c>UnitOfWorkPipelineBehavior</c>: saves a command's changes when it succeeds.</summary>
    UnitOfWork,

    /// <summary>
    /// <c>CachingPipelineBehavior</c>: answers an <c>ICachingQuery</c> from the cache, and caches its result. The
    /// innermost.
    /// </summary>
    Caching,

    /// <summary>
    /// <c>IdempotencyPipelineBehavior</c>: answers an <c>IIdempotentRequest</c> sent again with its key with the first
    /// one's result. Runs right after <see cref="Logging"/>: before <see cref="Validation"/>, so a repeat gets the first
    /// answer though a rule reading the data would refuse it now, and outside <see cref="Transaction"/> and
    /// <see cref="UnitOfWork"/>, so a result is kept only once committed and a repeat opens neither.
    /// </summary>
    Idempotency,

    /// <summary>
    /// <c>NonConcurrentPipelineBehavior</c>: handles an <c>INonConcurrentRequest</c> one at a time per request type and
    /// concurrency key; another waits until it has finished. Runs right after <see cref="Validation"/>, so an invalid
    /// request is refused without waiting; outside <see cref="Transaction"/> and <see cref="UnitOfWork"/>, so the next
    /// run starts once the previous one committed; and outside <see cref="Caching"/>, so a query that waited is answered
    /// from what the first one cached.
    /// </summary>
    NonConcurrent,
}
