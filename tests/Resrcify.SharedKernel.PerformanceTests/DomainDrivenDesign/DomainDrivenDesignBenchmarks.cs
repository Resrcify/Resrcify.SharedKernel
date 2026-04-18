using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.PerformanceTests.DomainDrivenDesign;

[MemoryDiagnoser]
public class DomainDrivenDesignBenchmarks
{
    private readonly EntityWithIntId _entityA = new(1);
    private readonly EntityWithIntId _entityB = new(1);
    private readonly NameValueObject _valueA = new("alpha");
    private readonly NameValueObject _valueB = new("alpha");
    private readonly int _state = Environment.CurrentManagedThreadId;

    [Benchmark(Baseline = true)]
    public bool Entity_Equals()
        => _entityA.Equals(_entityB);

    [Benchmark]
    public bool ValueObject_Equals()
        => _valueA.Equals(_valueB);

    [Benchmark]
    public int Aggregate_Raise_And_Clear_Events()
    {
        var aggregate = new AggregateWithEvents(1);
        aggregate.Publish(new TestDomainEvent(Guid.NewGuid()));
        aggregate.Publish(new TestDomainEvent(Guid.NewGuid()));
        var count = aggregate.GetDomainEvents().Count + (_state > 0 ? 0 : 1);
        aggregate.ClearDomainEvents();
        return count;
    }

    public static void SelfTest()
    {
        var instance = new DomainDrivenDesignBenchmarks();
        _ = instance.Entity_Equals();
        _ = instance.ValueObject_Equals();
        _ = instance.Aggregate_Raise_And_Clear_Events();
    }

    private sealed class EntityWithIntId(int id) : Entity<int>(id)
    {
    }

    private sealed class NameValueObject(string name) : ValueObject
    {
        public string Name { get; } = name;

        public override IEnumerable<object> GetAtomicValues()
        {
            yield return Name;
        }
    }

    private sealed class AggregateWithEvents(int id) : AggregateRoot<int>(id)
    {
        public void Publish(IDomainEvent domainEvent)
            => RaiseDomainEvent(domainEvent);
    }

    private sealed record TestDomainEvent : DomainEvent
    {
        public TestDomainEvent(Guid id)
            : base(id)
        {
        }
    }
}
