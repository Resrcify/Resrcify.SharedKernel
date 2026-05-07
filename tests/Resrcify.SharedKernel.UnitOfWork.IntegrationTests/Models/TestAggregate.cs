using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

internal sealed class TestAggregate
    : AggregateRoot<Guid>
{
    public TestAggregate(Guid id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public void ChangeName(string newName)
    {
        Name = newName;
        RaiseDomainEvent(new TestNameChangedEvent(Guid.NewGuid(), Id, newName));
    }
}

internal sealed record TestNameChangedEvent(
    Guid Id,
    Guid AggregateId,
    string NewName)
    : DomainEvent(Id);
