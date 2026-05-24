using System;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

internal sealed record TestDedupableDomainEvent(
    Guid Id,
    string ShardKey,
    string Message)
    : DomainEvent(Id), IDedupable
{
    public string DedupKey => ShardKey;
}

internal sealed record AnotherTestDedupableDomainEvent(
    Guid Id,
    string ShardKey)
    : DomainEvent(Id), IDedupable
{
    public string DedupKey => ShardKey;
}
