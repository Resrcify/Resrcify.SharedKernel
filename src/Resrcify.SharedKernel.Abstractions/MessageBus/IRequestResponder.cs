using System.Threading;
using System.Threading.Tasks;

using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// The responder side of scatter-gather: produces the reply to one request taken off a
/// rate-limited queue. Resolved in its own DI scope per message.
/// </summary>
/// <remarks>
/// Return a failure when there is no result to give (e.g. not found): its errors go back to the requester, which
/// records the item as failed with them; a failure that is the request's fault is answered at once, a transient one
/// (<c>Error.IsTransient()</c>) is tried again first. An exception is logged and treated as a transient failure: the
/// request goes back to its queue for another try (5 by default, each through the rate limiter), and after the last
/// one the requester gets a <c>&lt;TRequest&gt;.ResponderFailed</c> failure (<c>ErrorType.Failure</c>), not an
/// unanswered item. A request goes unanswered only when its requester stopped waiting (its timeout passed).
/// </remarks>
public interface IRequestResponder<in TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    Task<Result<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
