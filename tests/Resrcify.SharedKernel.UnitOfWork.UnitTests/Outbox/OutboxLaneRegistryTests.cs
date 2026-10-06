using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Outbox;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class OutboxLaneRegistryTests
{
    [Fact]
    public void Lanes_ShouldGroupEventTypesByLane_WhenOutboxLaneEventsAreRegistered()
    {
        var registry = BuildRegistry(services =>
        {
            services.AddSingleton(new OutboxLaneEvent("reports", typeof(TestDomainEvent)));
            services.AddSingleton(new OutboxLaneEvent("scatter-gather", typeof(SlowLookupRequested)));
        });

        registry.Lanes["reports"].ShouldBe([typeof(TestDomainEvent).FullName!]);
        registry.Lanes["scatter-gather"].ShouldBe([typeof(SlowLookupRequested).FullName!]);
        registry.LaneEventTypes.ShouldBe([typeof(TestDomainEvent).FullName!, typeof(SlowLookupRequested).FullName!], ignoreOrder: true);
    }

    [Fact]
    public void Lanes_ShouldIncludeALaneEvent_WhenItIsRegisteredAsTheInterface()
    {
        var registry = BuildRegistry(services =>
            services.AddSingleton<IOutboxLaneEvent>(new OutboxLaneEvent("reports", typeof(TestDomainEvent))));

        registry.Lanes["reports"].ShouldBe([typeof(TestDomainEvent).FullName!]);
    }

    [Fact]
    public void Lanes_ShouldListAnEventTypeOnce_WhenItIsRegisteredTwice()
    {
        var registry = BuildRegistry(services =>
        {
            services.AddSingleton(new OutboxLaneEvent("reports", typeof(TestDomainEvent)));
            services.AddSingleton(new OutboxLaneEvent("reports", typeof(TestDomainEvent)));
        });

        registry.Lanes["reports"].ShouldBe([typeof(TestDomainEvent).FullName!]);
    }

    [Fact]
    public void Lanes_ShouldBeEmpty_WhenNoOutboxLaneEventsAreRegistered()
        => BuildRegistry(services => { }).Lanes.ShouldBeEmpty();

    private static OutboxLaneRegistry BuildRegistry(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddOutboxLanes<TestDbContext>();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<OutboxLaneRegistry>();
    }

    internal sealed record SlowLookupRequested(Guid Id);
}
