using Newtonsoft.Json;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// <see cref="IOutboxSerializer"/> backed by <c>Newtonsoft.Json</c>. The concrete
/// event type travels in the payload via <see cref="TypeNameHandling.All"/>.
/// </summary>
public sealed class NewtonsoftJsonOutboxSerializer
    : IOutboxSerializer
{
    private static readonly JsonSerializerSettings _settings = new()
    {
        TypeNameHandling = TypeNameHandling.All
    };

    public string Serialize(IDomainEvent domainEvent)
        => JsonConvert.SerializeObject(domainEvent, _settings);

    public IDomainEvent? Deserialize(string content)
        => JsonConvert.DeserializeObject<IDomainEvent>(content, _settings);
}
