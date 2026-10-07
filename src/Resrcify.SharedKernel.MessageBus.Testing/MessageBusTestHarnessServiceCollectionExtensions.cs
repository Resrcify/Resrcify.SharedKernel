using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.Testing;

public static class MessageBusTestHarnessServiceCollectionExtensions
{
    /// <summary>
    /// Switches the service's message bus (<c>AddMessageBus</c>, called before this) to <paramref name="network"/>, in
    /// memory, whatever transport it was given (RabbitMQ too: nothing connects to it), and registers a
    /// <see cref="MessageBusTestHarness"/> that records every bus of the service. Pass one network to every service of a
    /// test so they reach each other; without one the service gets a network of its own.
    /// </summary>
    /// <example>
    /// <code>
    /// // In a WebApplicationFactory: builder.ConfigureTestServices(services =&gt; services.AddMessageBusTestHarness(network));
    /// var harness = factory.Services.GetRequiredService&lt;MessageBusTestHarness&gt;();
    /// await client.PostAsJsonAsync("/shards", request);
    /// var published = await harness.Published.WaitForAsync&lt;ShardCreated&gt;(e =&gt; e.Name == "Main");
    /// </code>
    /// </example>
    public static IServiceCollection AddMessageBusTestHarness(
        this IServiceCollection services,
        MessageBusTestNetwork? network = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(MessageBusTestHarness)))
            throw new InvalidOperationException("AddMessageBusTestHarness was already called for this service.");
        var settings = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(MessageBusSettings))?
            .ImplementationInstance as MessageBusSettings
            ?? throw new InvalidOperationException(
                "Call AddMessageBus before AddMessageBusTestHarness: the harness switches that bus to the in-memory network.");

        network ??= new MessageBusTestNetwork();
        settings.UseInMemory(network.Network);
        var harness = new MessageBusTestHarness(network);
        services.AddSingleton(harness);
        services.AddSingleton<IBusInstrumentation>(new HarnessInstrumentation(harness));
        return services;
    }
}
