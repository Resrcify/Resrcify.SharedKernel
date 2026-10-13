using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Handlers;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Shouldly;
using Xunit;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class IntegrationEventRegistrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddEventHandlers_ShouldWireTheHandlerAndSubscribe_WhenTheMediatorScansItsAssembly(bool mediatorFirst)
    {
        var services = new ServiceCollection();
        if (mediatorFirst)
            services.AddMediator(typeof(IntegrationEventRegistrationTests).Assembly);
        services.AddMessageBus(bus => bus.UseInMemory().AddEventHandlers());
        if (!mediatorFirst)
            services.AddMediator(typeof(IntegrationEventRegistrationTests).Assembly);

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IIntegrationEventHandler<ShardRenamed>) &&
            descriptor.ImplementationType == typeof(ShardRenamedHandler));
        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IHandleMessages<ShardRenamed>) &&
            descriptor.ImplementationType == typeof(IntegrationEventDispatcher<ShardRenamed>));
        SubscribedEvents(services).ShouldContain(typeof(ShardRenamed));
    }

    [Fact]
    public void AddEventHandlers_ShouldRegisterEachHandlerOnce_WhenItsAssemblyIsScannedMoreThanOnce()
    {
        var assembly = typeof(IntegrationEventRegistrationTests).Assembly;
        var services = new ServiceCollection();
        services.AddMediator(assembly);
        services.AddMessageBus(bus => bus.UseInMemory().AddEventHandlers(assembly));
        services.AddMediator(assembly);

        services.Count(descriptor => descriptor.ServiceType == typeof(IHandleMessages<ShardRenamed>)).ShouldBe(1);
        SubscribedEvents(services).Count(eventType => eventType == typeof(ShardRenamed)).ShouldBe(1);
    }

    [Fact]
    public void AddMessageBus_ShouldRegisterTheEventBus_WithoutAddEventHandlers()
        => new ServiceCollection()
            .AddMessageBus(bus => bus.UseInMemory())
            .ShouldContain(descriptor => descriptor.ServiceType == typeof(IEventBus));

    private static System.Collections.Generic.List<System.Type> SubscribedEvents(ServiceCollection services)
        => [.. services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SubscribedEvent>()
            .Select(subscribed => subscribed.EventType)];

    internal sealed record ShardRenamed(string Name);

    internal sealed class ShardRenamedHandler : IIntegrationEventHandler<ShardRenamed>
    {
        public Task<Result> HandleAsync(ShardRenamed integrationEvent, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }
}
