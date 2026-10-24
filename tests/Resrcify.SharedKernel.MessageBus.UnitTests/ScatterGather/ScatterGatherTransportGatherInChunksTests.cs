using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.UnitTests.Hosting;
using Shouldly;
using Xunit;
using static Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather.ScatterGatherTransportTests;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

/// <summary>
/// The message bus' client's chunked gather, on the in-memory transport against a real responder: the chunks come back
/// answered, in order, and the budget runs on the container's clock (a fake one here, moved only by the test).
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ScatterGatherTransportGatherInChunksTests
{
    [Fact]
    public async Task GatherInChunksAsync_ShouldGatherEveryChunkInOrder_FromTheResponder()
    {
        var (responder, requester, calls) = await StartAsync(Clock());
        using (responder)
        using (requester)
        {
            var chunks = new List<GatheredChunk<Echoed>>();
            await foreach (var chunk in Client(requester).GatherInChunksAsync<Echo, Echoed>(Requests(5), 2, _ => TimeSpan.FromMinutes(1)))
                chunks.Add(chunk);

            chunks.Select(chunk => string.Join(",", chunk.Keys)).ShouldBe(["0,1", "2,3", "4"]);
            chunks.ShouldAllBe(chunk => chunk.IsGathered && chunk.Gathered.IsComplete);
            chunks[2].Gathered!.Results["4"].Text.ShouldBe("4");
            chunks.Aggregate(GatheredCounts.None, (sum, chunk) => sum + chunk.Counts).Answered.ShouldBe(5);
            calls.Count.ShouldBe(5);
        }
    }

    [Fact]
    public async Task GatherInChunksAsync_ShouldMeasureTheBudgetOnTheContainersClock()
    {
        var time = Clock();
        var (responder, requester, calls) = await StartAsync(time);
        using (responder)
        using (requester)
        {
            var chunks = new List<GatheredChunk<Echoed>>();
            await foreach (var chunk in Client(requester).GatherInChunksAsync<Echo, Echoed>(
                Requests(4),
                1,
                _ => TimeSpan.FromSeconds(4),
                budget: TimeSpan.FromSeconds(9)))
            {
                chunks.Add(chunk);
                // The caller's work on each chunk, 3 s on the container's clock, while the next chunk is gathered (it
                // started as the one before was gathered: the second at 0 s, the third at 3 s), so the clock never
                // reaches the timeout of a chunk under way. The fourth would start at 6 s and end at 10 s, past the
                // 9 s budget. On the system clock nothing would have gone.
                time.Advance(TimeSpan.FromSeconds(3));
            }

            chunks.Select(chunk => chunk.Status)
                .ShouldBe([GatheredChunkStatus.Gathered, GatheredChunkStatus.Gathered, GatheredChunkStatus.Gathered, GatheredChunkStatus.NotAsked]);
            chunks.Take(3).ShouldAllBe(chunk => chunk.Gathered!.IsComplete);
            chunks[3].Keys.ShouldBe(["3"]);
            calls.Count.ShouldBe(3);
        }
    }

    private static async Task<(IHost Responder, IHost Requester, EchoCalls Calls)> StartAsync(FakeTimeProvider time)
    {
        var network = new InMemNetwork();
        var queue = $"echo-{Guid.NewGuid():N}";
        var calls = new EchoCalls();
        var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<Echo, Echoed, EchoHandler>(queue),
            services => services.AddSingleton(calls));
        var requester = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRequest<Echo, Echoed>(queue).AddScatterGather(),
            services => services.AddSingleton<TimeProvider>(time));
        return (responder, requester, calls);
    }

    // At the real time: a request's deadline is set on the sender's clock and read on the responder's (the system
    // clock), so a fake clock years behind (FakeTimeProvider's default start) would have every request dropped.
    private static FakeTimeProvider Clock()
        => new(TimeProvider.System.GetUtcNow());

    private static IScatterGatherClient Client(IHost host)
        => host.Services.GetRequiredService<IScatterGatherClient>();

    private static Dictionary<string, Echo> Requests(int count)
        => Enumerable.Range(0, count)
            .ToDictionary(
                index => index.ToString(CultureInfo.InvariantCulture),
                index => new Echo(index.ToString(CultureInfo.InvariantCulture)),
                StringComparer.Ordinal);
}
