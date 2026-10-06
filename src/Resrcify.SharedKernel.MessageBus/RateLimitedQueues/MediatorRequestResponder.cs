using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// Answers a rate-limited queue's requests through the mediator: each becomes the mediator request
/// <c>toMediatorRequest</c> makes, and its handler's result is the answer. Whether a failure is answered at once or
/// tried again is the queue host's decision (<see cref="RateLimitedQueueOptions.AnswerFailure"/>), as for any responder.
/// </summary>
internal sealed class MediatorRequestResponder<TRequest, TResponse>(
    ISender sender,
    Func<TRequest, IRequest<Result<TResponse>>> toMediatorRequest)
    : IRequestResponder<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    public Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        => sender.Send(toMediatorRequest(request), cancellationToken);
}
