using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// Publishes integration events: every service that subscribes gets a copy, and within a service one instance
/// handles it. The event goes out under its wire name (its class name, without the namespace), so publisher and
/// subscribers each keep their own class for it, with the same name.
/// </summary>
/// <remarks>
/// Publish from a domain event handler, so the outbox runs it: the domain event commits with the change that
/// raised it, and is retried until the publish succeeds. Delivery is at-least-once (a retried handler publishes
/// again), so subscribers must tolerate a duplicate.
/// </remarks>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class;
}
