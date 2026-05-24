using System;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

internal sealed record TestDedupableEvent(
    Guid Id,
    string ShardKey)
    : DomainEvent(Id), IDedupable
{
    public string DedupKey => ShardKey;
}
