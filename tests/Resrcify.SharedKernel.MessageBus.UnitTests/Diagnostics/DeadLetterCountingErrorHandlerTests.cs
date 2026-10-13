using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Diagnostics;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DeadLetterCountingErrorHandlerTests
{
    [Fact]
    public async Task HandlePoisonMessage_ShouldCountTheMessage_WhenAnEventKeepsFailing()
    {
        using var metrics = new MetricsCapture();
        var network = new InMemNetwork();
        var queue = $"subscriber-{Guid.NewGuid():N}";
        var log = new PayoutLog { AlwaysFail = true };
        using var subscriber = await InMemoryServices.StartAsync(
            network,
            bus => bus.WithInputQueue(queue).AddEventHandlers(typeof(DeadLetterCountingErrorHandlerTests).Assembly),
            services => services.AddSingleton(log));
        using var publisher = await InMemoryServices.StartAsync(network, _ => { });

        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new PayoutRotated("shard-1", 1));

        // 5 tries in place, 0.5 + 1 + 2 + 4 s apart.
        await InMemoryServices.WaitUntilAsync(() => network.Count($"{queue}.error") == 1, TimeSpan.FromSeconds(15));
        metrics.Count($"messagebus.messages.dead_lettered message=PayoutRotated queue={queue}").ShouldBe(1);
        log.Attempts.ShouldBe(5);
    }

    [Fact]
    public async Task HandlePoisonMessage_ShouldCountAnUnknownEvent_WhenTheServiceNoLongerHandlesIt()
    {
        using var metrics = new MetricsCapture();
        var network = new InMemNetwork();
        var queue = $"subscriber-{Guid.NewGuid():N}";
        using (await InMemoryServices.StartAsync(
            network,
            bus => bus.WithInputQueue(queue).AddEventHandlers(typeof(DeadLetterCountingErrorHandlerTests).Assembly),
            services => services.AddSingleton(new PayoutLog())))
        {
            // Subscribed; the next release deletes the handler but forgets RemoveSubscription.
        }
        using var next = await InMemoryServices.StartAsync(network, bus => bus.WithInputQueue(queue));
        using var publisher = await InMemoryServices.StartAsync(network, _ => { });

        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new PayoutRotated("shard-1", 1));

        await InMemoryServices.WaitUntilAsync(() => network.Count($"{queue}.error") == 1);
        metrics.Count("messagebus.messages.unknown message=PayoutRotated").ShouldBeGreaterThanOrEqualTo(1);
        metrics.Count($"messagebus.messages.dead_lettered message=PayoutRotated queue={queue}").ShouldBe(1);
    }
}
