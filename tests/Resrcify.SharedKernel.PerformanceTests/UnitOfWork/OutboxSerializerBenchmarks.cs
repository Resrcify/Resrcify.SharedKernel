using System;
using BenchmarkDotNet.Attributes;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.PerformanceTests.UnitOfWork;

/// <summary>
/// The <see cref="IOutboxSerializer"/> round trip the outbox actually performs: serialize on
/// write (interceptor), deserialize on read (job).
/// </summary>
[MemoryDiagnoser]
public class OutboxSerializerBenchmarks
{
    private readonly SystemTextJsonOutboxSerializer _systemTextJson = new();
    private OutboxBenchmarkEvent _event = default!;
    private string _systemTextJsonPayload = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _event = new OutboxBenchmarkEvent(Guid.NewGuid(), "outbox-benchmark", 42);
        _systemTextJsonPayload = _systemTextJson.Serialize(_event);
    }

    [Benchmark]
    public string SystemTextJson_Serialize()
        => _systemTextJson.Serialize(_event);

    [Benchmark]
    public IDomainEvent? SystemTextJson_Deserialize()
        => _systemTextJson.Deserialize(_systemTextJsonPayload);

    public static void SelfTest()
    {
        var benchmarks = new OutboxSerializerBenchmarks();
        benchmarks.GlobalSetup();
        _ = benchmarks.SystemTextJson_Serialize();
        _ = benchmarks.SystemTextJson_Deserialize();
    }
}

public sealed record OutboxBenchmarkEvent(
    Guid Id,
    string Name,
    int Sequence)
    : DomainEvent(Id);
