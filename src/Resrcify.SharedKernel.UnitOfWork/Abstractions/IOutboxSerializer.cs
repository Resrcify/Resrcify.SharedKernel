using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.UnitOfWork.Abstractions;

/// <summary>
/// Strategy for turning domain events into outbox payloads and back. Both built-in
/// implementations embed the concrete type in the payload, so <see cref="Deserialize"/>
/// needs only the stored content. Pick one by registering it as <see cref="IOutboxSerializer"/>:
/// <c>SystemTextJsonOutboxSerializer</c> or <c>NewtonsoftJsonOutboxSerializer</c>.
/// </summary>
public interface IOutboxSerializer
{
    /// <summary>Serializes a domain event into the outbox message content payload.</summary>
    string Serialize(IDomainEvent domainEvent);

    /// <summary>Rebuilds the concrete domain event from a stored payload.</summary>
    IDomainEvent? Deserialize(string content);
}
