using System.Linq;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rebus.Compression;
using Resrcify.SharedKernel.MessageBus.Serialization;

namespace Resrcify.SharedKernel.PerformanceTests.MessageBus;

/// <summary>
/// The bus' gzip of a large body (a roster-sized JSON message, 755 KB: 189 KB at Optimal, 313 KB at Fastest)
/// against Rebus' own <see cref="Zipper"/> (<c>EnableCompression</c>): Optimal level and a growing stream, versus
/// Fastest and a buffer sized from the gzip trailer. The first two unzip benchmarks read the same Rebus-zipped body,
/// so they compare only the reading.
/// </summary>
[MemoryDiagnoser]
public class MessageCompressionBenchmarks
{
    private readonly Zipper _rebus = new();
    private byte[] _body = default!;
    private byte[] _zippedByRebus = default!;
    private byte[] _zippedFastest = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _body = RosterJson();
        _zippedByRebus = _rebus.Zip(_body);
        _zippedFastest = MessageCompression.Zip(_body);
    }

    [Benchmark(Baseline = true)]
    public byte[] Rebus_Zip_Optimal()
        => _rebus.Zip(_body);

    [Benchmark]
    public byte[] SharedKernel_Zip_Fastest()
        => MessageCompression.Zip(_body);

    [Benchmark]
    public byte[] Rebus_Unzip_Growing()
        => _rebus.Unzip(_zippedByRebus);

    [Benchmark]
    public byte[] SharedKernel_Unzip_Presized()
        => MessageCompression.Unzip(_zippedByRebus);

    // What a receiver of this package's messages does: the Fastest body is larger to read.
    [Benchmark]
    public byte[] SharedKernel_Unzip_Presized_FastestBody()
        => MessageCompression.Unzip(_zippedFastest);

    public static void SelfTest()
    {
        var benchmarks = new MessageCompressionBenchmarks();
        benchmarks.GlobalSetup();
        _ = benchmarks.Rebus_Zip_Optimal();
        _ = benchmarks.SharedKernel_Zip_Fastest();
        _ = benchmarks.Rebus_Unzip_Growing();
        _ = benchmarks.SharedKernel_Unzip_Presized();
        _ = benchmarks.SharedKernel_Unzip_Presized_FastestBody();
    }

    // A player's roster as a service sends it: ~300 units, each with stats and mods; names and keys repeat, numbers vary.
    private static byte[] RosterJson()
    {
        var units = Enumerable.Range(0, 300).Select(unit => new
        {
            definitionId = $"UNIT_{unit % 240:D3}:SEVEN_STAR",
            level = 85,
            gear = Vary(unit, 1, 14),
            relic = Vary(unit + 1, 0, 10),
            stats = Enumerable.Range(1, 60).ToDictionary(stat => $"stat_{stat}", stat => Vary(unit * 61 + stat, 0, 1_000_000)),
            mods = Enumerable.Range(0, 6).Select(slot => new
            {
                id = $"{Vary(unit * 7 + slot, 0, int.MaxValue):x8}{Vary(unit * 11 + slot, 0, int.MaxValue):x8}",
                slot,
                set = Vary(unit + slot, 1, 9),
                primary = new { stat = Vary(unit * 3 + slot, 1, 60), value = Vary(unit * 5 + slot, 0, 300_000) },
                secondaries = Enumerable.Range(0, 4).Select(roll => new
                {
                    stat = Vary(unit * 13 + slot * 4 + roll, 1, 60),
                    value = Vary(unit * 17 + slot * 4 + roll, 0, 30_000),
                    rolls = Vary(unit * 19 + slot * 4 + roll, 1, 6),
                }),
            }),
        });
        return JsonSerializer.SerializeToUtf8Bytes(new { allyCode = 123456789, units });
    }

    // A fixed spread of values (Knuth's multiplicative hash): the same body on every run, without a random generator.
    private static int Vary(
        int seed,
        int min,
        int max)
        => min + (int)(unchecked((uint)seed * 2654435761u) % (uint)(max - min));
}
