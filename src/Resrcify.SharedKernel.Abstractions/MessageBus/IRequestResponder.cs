using System.Threading;
using System.Threading.Tasks;

using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// The responder side of scatter-gather: produces the reply to one request taken off a
/// rate-limited queue. Resolved in its own DI scope per message.
/// </summary>
/// <remarks>
/// Return a failure when there is no result to give (e.g. not found): its errors go back to the requester,
/// which records the item as failed with them. Throw for a transient problem: the message is retried, and when
/// the retries are exhausted it is dropped, so the requester sees the item as unanswered.
/// </remarks>
public interface IRequestResponder<in TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    Task<Result<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
