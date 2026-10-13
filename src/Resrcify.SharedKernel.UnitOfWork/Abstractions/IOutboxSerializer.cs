using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.UnitOfWork.Abstractions;

/// <summary>
/// Strategy for turning domain events into outbox payloads and back. The built-in
/// <c>SystemTextJsonOutboxSerializer</c> embeds the concrete type in the payload, so <see cref="Deserialize"/>
/// needs only the stored content. Implement this interface for another format.
/// </summary>
public interface IOutboxSerializer
{
    /// <summary>Serializes a domain event into the outbox message content payload.</summary>
    string Serialize(IDomainEvent domainEvent);

    /// <summary>Rebuilds the concrete domain event from a stored payload.</summary>
    IDomainEvent? Deserialize(string content);
}
