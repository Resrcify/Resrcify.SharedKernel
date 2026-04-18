using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Repository.Primitives;

namespace Resrcify.SharedKernel.PerformanceTests.Repository;

[MemoryDiagnoser]
public class RepositoryBenchmarks
{
    private IQueryable<PersonAggregate> _persons = default!;
    private PersonByMinAgeSpecification _specification = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var items = new List<PersonAggregate>(capacity: 512);
        for (var index = 0; index < 512; index++)
            items.Add(new PersonAggregate(index, index % 100));

        _persons = items.AsQueryable();
        _specification = new PersonByMinAgeSpecification(18);
    }

    [Benchmark(Baseline = true)]
    public int SpecificationEvaluator_Count()
        => SpecificationEvaluator
            .GetQuery(_persons, _specification)
            .Count();

    [Benchmark]
    public int Linq_Count()
        => _persons.Count(person => person.Age >= 18);

    public static void SelfTest()
    {
        var instance = new RepositoryBenchmarks();
        instance.GlobalSetup();
        _ = instance.SpecificationEvaluator_Count();
        _ = instance.Linq_Count();
    }

    private sealed class PersonAggregate : IAggregateRoot<int>
    {
        public PersonAggregate(int id, int age)
        {
            Id = id;
            Age = age;
        }

        public int Id { get; }

        public int Age { get; }

        public IReadOnlyList<IDomainEvent> GetDomainEvents()
            => [];

        public void ClearDomainEvents()
        {
        }
    }

    private sealed class PersonByMinAgeSpecification : Specification<PersonAggregate, int>
    {
        public PersonByMinAgeSpecification(int minimumAge)
            : base(person => person.Age >= minimumAge)
        {
        }
    }
}
