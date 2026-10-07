using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Bus;
using Rebus.Handlers;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.Testing;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Testing;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusTestNetworkTests
{
    [Fact]
    public async Task WaitUntilIdleAsync_ShouldWaitForWhatHandlersSentInTurn()
    {
        // The first handler sends the next message; the last one takes a while.
        var network = new MessageBusTestNetwork();
        var steps = new Steps();
        using var host = await StartAsync(network, steps);

        await host.Services.GetRequiredService<IBus>().Send(new Step(3));
        await network.WaitUntilIdleAsync();

        steps.Done.ShouldBe(3);
    }

    [Fact]
    public async Task WaitUntilIdleAsync_ShouldSayWhatIsBusy_WhenAHandlerDoesNotFinishInTime()
    {
        var network = new MessageBusTestNetwork();
        var steps = new Steps { Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var host = await StartAsync(network, steps);
        await host.Services.GetRequiredService<IBus>().Send(new Step(1));

        var timeout = await Should.ThrowAsync<TimeoutException>(() => network.WaitUntilIdleAsync(TimeSpan.FromMilliseconds(300)));

        timeout.Message.ShouldContain("1 message(s) being handled");
        steps.Hold.SetResult();
        await network.WaitUntilIdleAsync();
    }

    [Fact]
    public async Task StartResponderAsync_ShouldAnswerRequests_InPlaceOfTheOtherService()
    {
        var network = new MessageBusTestNetwork();
        await using var playerService = await network.StartResponderAsync<GetPlayer, PlayerProfile>(
            (request, _) => Task.FromResult(Result.Success(new PlayerProfile(request.AllyCode, "Han"))),
            queue: "players");
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMessageBus(bus => bus.AddRequest<GetPlayer, PlayerProfile>("players").AddScatterGather());
        builder.Services.AddMessageBusTestHarness(network);
        using var requester = builder.Build();
        await requester.StartAsync();

        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<GetPlayer, PlayerProfile>(new GetPlayer("123456789"), TimeSpan.FromSeconds(5));

        reply.Value.ShouldBe(new PlayerProfile("123456789", "Han"));
        playerService.Harness.Consumed.Of<GetPlayer>().ShouldHaveSingleItem().AllyCode.ShouldBe("123456789");
    }

    private static async Task<IHost> StartAsync(MessageBusTestNetwork network, Steps steps)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(steps);
        builder.Services.AddMessageBus(bus => bus
            .WithInputQueue("steps")
            .AddMessage<Step>("test.step.v1", sendTo: "steps")
            .AddHandler<StepHandler>());
        builder.Services.AddMessageBusTestHarness(network);
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    internal sealed record Step(int Left);

    internal sealed record GetPlayer(string AllyCode);

    internal sealed record PlayerProfile(string AllyCode, string Name);

    internal sealed class Steps
    {
        private int _done;

        public TaskCompletionSource? Hold { get; init; }

        public int Done
            => Volatile.Read(ref _done);

        public void Add()
            => Interlocked.Increment(ref _done);
    }

    internal sealed class StepHandler(IBus bus, Steps steps) : IHandleMessages<Step>
    {
        public async Task Handle(Step message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (steps.Hold is { } hold)
                await hold.Task;
            if (message.Left == 1)
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            steps.Add();
            if (message.Left > 1)
                await bus.Send(new Step(message.Left - 1));
        }
    }
}
