using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ScatterGatherRequestHandlerTests
{
    [Fact]
    public async Task Send_ShouldGatherTheRepliesBeforeTheHandlerRuns_WhenTheRequestHasAScatterGatherHandler()
    {
        var transport = new FakeScatterGatherTransport(key => new Pong("pong " + key));
        await using var provider = Build(transport);

        var answered = await provider.GetRequiredService<ISender>().Send(new CountPongs(Items: 3));

        answered.ShouldBe(3);
        transport.Sent.Keys.Order().ShouldBe(["0", "1", "2"]);
    }

    [Fact]
    public async Task Send_ShouldHandleWithoutAsking_WhenThereIsNothingToScatter()
    {
        var transport = new FakeScatterGatherTransport(key => new Pong("pong " + key));
        await using var provider = Build(transport);

        var answered = await provider.GetRequiredService<ISender>().Send(new CountPongs(Items: 0));

        answered.ShouldBe(0);
        transport.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Send_ShouldHandTheRepliesOverAsTheyArrive_WhenTheHandlerStreamsThem()
    {
        var transport = new FakeScatterGatherTransport(key => new Pong("pong " + key));
        await using var provider = Build(transport);

        var firstTwo = await provider.GetRequiredService<ISender>().Send(new FirstPongs(Items: 5, Wanted: 2));

        firstTwo.ShouldBe(["pong 0", "pong 1"]);
        // The handler stopped after two: the rest were never waited for.
        transport.Streamed.Count.ShouldBe(2);
    }

    private static ServiceProvider Build(FakeScatterGatherTransport transport)
    {
        var services = new ServiceCollection();
        services.AddMediator(typeof(ScatterGatherRequestHandlerTests).Assembly);
        services.AddMessageBus(bus => bus
            .UseRabbitMq(new RabbitMqConnection("localhost", 5672, "guest", "guest"))
            .AddScatterGather());
        services.AddSingleton<IScatterGatherClient>(transport);
        return services.BuildServiceProvider();
    }

    internal sealed record Ping(string Value);

    internal sealed record Pong(string Value);

    internal sealed record CountPongs(int Items) : IRequest<int>;

    internal sealed record FirstPongs(int Items, int Wanted) : IRequest<IReadOnlyList<string>>;

    internal sealed class CountPongsHandler : IScatterGatherRequestHandler<CountPongs, Ping, Pong, int>
    {
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);

        public Task<IReadOnlyDictionary<string, Ping>> ScatterAsync(CountPongs request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, Ping>>(
                Enumerable.Range(0, request.Items).ToDictionary(i => i.ToString(CultureInfo.InvariantCulture), i => new Ping($"ping {i}")));

        public Task<int> HandleAsync(CountPongs request, IGathered<Pong> replies, CancellationToken cancellationToken)
            => Task.FromResult(replies.Results.Count);
    }

    internal sealed class FirstPongsHandler : IStreamingScatterGatherRequestHandler<FirstPongs, Ping, Pong, IReadOnlyList<string>>
    {
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);

        public Task<IReadOnlyDictionary<string, Ping>> ScatterAsync(FirstPongs request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, Ping>>(
                Enumerable.Range(0, request.Items).ToDictionary(i => i.ToString(CultureInfo.InvariantCulture), i => new Ping($"ping {i}")));

        public async Task<IReadOnlyList<string>> HandleAsync(FirstPongs request, IAsyncEnumerable<IScatterReply<Pong>> replies, CancellationToken cancellationToken)
        {
            var seen = new List<string>();
            await foreach (var reply in replies.WithCancellation(cancellationToken))
            {
                seen.Add(reply.Result.Value.Value);
                if (seen.Count == request.Wanted)
                    break;
            }
            return seen;
        }
    }
}
