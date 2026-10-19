using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Hosts;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Messages;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests;

/// <summary>
/// <c>UseRabbitMqFromConfiguration</c> over real RabbitMQ: a service given the <c>MessageBus:*</c> settings
/// (<c>ServiceHostFixture</c>'s keys, its appsettings', its Kubernetes manifest's) connects with them, on a virtual host
/// of its own whose queues are quorum queues, as on a production cluster.
/// </summary>
[Collection(BusCollection.Name)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class RabbitMqOptionsTests(BusFixture bus)
{
    private readonly string _requestQueue = $"ping-{Guid.NewGuid():N}";

    [Fact]
    public async Task UseRabbitMqFromConfiguration_ConnectsWithTheMessageBusSection()
    {
        var quorum = await bus.QuorumVirtualHostAsync();
        await using var pod = await ResponderPod.StartAsync(quorum, _requestQueue, perSecond: 100, burst: 10);
        await using var requester = await ConfiguredRequesterHost.StartAsync(
            ConfiguredRequesterHost.SettingsFor(quorum),
            _requestQueue);

        var reply = await requester.RequestAsync(new PingRequest("configured"), TimeSpan.FromSeconds(15));

        reply.Value.ShouldBe(new PingResponse("pong configured"));
    }

    [Fact]
    public async Task UseRabbitMqFromConfiguration_FailsTheStartNamingTheKey_WhenTheSectionHasNoHost()
    {
        var settings = ConfiguredRequesterHost.SettingsFor(bus.RabbitMqConnection);
        settings.Remove("MessageBus:Host");

        var failure = await Should.ThrowAsync<OptionsValidationException>(
            () => ConfiguredRequesterHost.StartAsync(settings, _requestQueue));

        failure.Failures.ShouldBe(["MessageBus:Host is required."]);
    }
}
