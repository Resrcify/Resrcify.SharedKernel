using System.Text.Json;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Converters;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// <see cref="IOutboxSerializer"/> backed by <c>System.Text.Json</c>. The concrete
/// event type travels in a <c>$type</c> property written by <see cref="DomainEventConverter"/>.
/// </summary>
public sealed class SystemTextJsonOutboxSerializer
    : IOutboxSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        Converters = { new DomainEventConverter() }
    };

    public string Serialize(IDomainEvent domainEvent)
        => JsonSerializer.Serialize(domainEvent, _options);

    public IDomainEvent? Deserialize(string content)
        => JsonSerializer.Deserialize<IDomainEvent>(content, _options);
}
