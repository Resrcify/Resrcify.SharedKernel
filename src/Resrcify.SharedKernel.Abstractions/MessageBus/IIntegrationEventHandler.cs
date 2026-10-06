using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// Handles an integration event another service published (<see cref="IEventBus"/>). Found by the message bus'
/// <c>AddEventHandlers</c> in the assemblies the mediator scans; having one subscribes this service to the event.
/// </summary>
/// <remarks>
/// <para>
/// The handler says how it went with its <see cref="Result"/>, without throwing:
/// <list type="bullet">
/// <item>a success: done;</item>
/// <item>a failure that is the event's fault (every error <c>NotFound</c>, <c>Validation</c>, <c>Conflict</c>,
/// <c>Unauthorized</c> or <c>Forbidden</c>): another try would fail the same way, so it is logged and not retried;</item>
/// <item>any other failure (<c>Failure</c>, <c>ExternalFailure</c>, <c>Timeout</c>, <c>RateLimit</c>): tried again in
/// place (5 tries, waiting 0.5, 1, 2 and 4 s), then moved to the service's error queue.</item>
/// </list>
/// An exception is treated like the last kind. Delivery is at-least-once, so an event can arrive twice: make handling
/// it twice harmless. The token fires when the service stops.
/// </para>
/// </remarks>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task<Result> HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
