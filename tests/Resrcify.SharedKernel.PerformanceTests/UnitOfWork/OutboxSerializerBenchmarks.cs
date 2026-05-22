using System;
using BenchmarkDotNet.Attributes;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.PerformanceTests.UnitOfWork;

/// <summary>
/// Compares the two <see cref="IOutboxSerializer"/> strategies on the round trip the
/// outbox actually performs: serialize on write (interceptor), deserialize on read (job).
/// </summary>
[MemoryDiagnoser]
public class OutboxSerializerBenchmarks
{
    private readonly SystemTextJsonOutboxSerializer _systemTextJson = new();
    private readonly NewtonsoftJsonOutboxSerializer _newtonsoft = new();
    private OutboxBenchmarkEvent _event = default!;
    private string _systemTextJsonPayload = default!;
    private string _newtonsoftPayload = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _event = new OutboxBenchmarkEvent(Guid.NewGuid(), "outbox-benchmark", 42);
        _systemTextJsonPayload = _systemTextJson.Serialize(_event);
        _newtonsoftPayload = _newtonsoft.Serialize(_event);
    }

    [Benchmark(Baseline = true)]
    public string SystemTextJson_Serialize()
        => _systemTextJson.Serialize(_event);

    [Benchmark]
    public string Newtonsoft_Serialize()
        => _newtonsoft.Serialize(_event);

    [Benchmark]
    public IDomainEvent? SystemTextJson_Deserialize()
        => _systemTextJson.Deserialize(_systemTextJsonPayload);

    [Benchmark]
    public IDomainEvent? Newtonsoft_Deserialize()
        => _newtonsoft.Deserialize(_newtonsoftPayload);

    public static void SelfTest()
    {
        var benchmarks = new OutboxSerializerBenchmarks();
        benchmarks.GlobalSetup();
        _ = benchmarks.SystemTextJson_Serialize();
        _ = benchmarks.Newtonsoft_Serialize();
        _ = benchmarks.SystemTextJson_Deserialize();
        _ = benchmarks.Newtonsoft_Deserialize();
    }
}

public sealed record OutboxBenchmarkEvent(
    Guid Id,
    string Name,
    int Sequence)
    : DomainEvent(Id);
