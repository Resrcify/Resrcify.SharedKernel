using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// Handles an <see cref="INonConcurrentRequest"/> one at a time: while one is handled, another of the same request type
/// and <see cref="INonConcurrentRequest.ConcurrencyKey"/> waits until it has finished, and is then handled itself.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>It serialises, it doesn't coalesce: each request is handled, and gets its own result.</item>
/// <item>Per process: the locks are the mediator's (a singleton), not shared with other instances of the service.</item>
/// <item>A request waits as long as it must; cancelled while waiting, it isn't handled and the send throws
/// <see cref="OperationCanceledException"/>.</item>
/// <item>The lock is released however the request ends: a result, a failure, an exception or a cancellation.</item>
/// </list>
/// A standard behavior, right after validation: an invalid request is refused without waiting, and the transaction and
/// the unit of work run inside the lock, so the next run starts once the previous one has committed and sees what it
/// saved. And outside caching: a query that waited is answered from what the first one cached.
/// </remarks>
public sealed class NonConcurrentPipelineBehavior<TRequest, TResponse>(
    IServiceProvider services)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, INonConcurrentRequest
    where TResponse : Result
{
    // A singleton the mediator registers (AddMediator); the behavior itself is transient.
    private readonly NonConcurrentLocks _locks = services.GetRequiredService<NonConcurrentLocks>();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        using var lease = await _locks.AcquireAsync(
            typeof(TRequest),
            request.ConcurrencyKey,
            cancellationToken);

        return await next(cancellationToken);
    }
}
