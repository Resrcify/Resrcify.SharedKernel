using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Abstractions.MessageBus;

namespace Resrcify.SharedKernel.MessageBus.Testing;

public static class ScatterGatherClientStandInServiceCollectionExtensions
{
    /// <summary>
    /// Makes <paramref name="standIn"/> the host's <see cref="IScatterGatherClient"/>, in place of the message bus'
    /// client: every call is answered as the test scripted it (e.g. in a <c>ServiceHostFixture</c>'s
    /// <c>ConfigureTestServices</c>).
    /// </summary>
    public static IServiceCollection ReplaceScatterGatherClient(
        this IServiceCollection services,
        ScatterGatherClientStandIn standIn)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(standIn);
        services.RemoveAll<IScatterGatherClient>();
        services.AddSingleton<IScatterGatherClient>(standIn);
        return services;
    }

    /// <summary>
    /// Puts <paramref name="standIn"/> in front of the host's own <see cref="IScatterGatherClient"/> (the message bus'):
    /// every call is recorded, held while the test holds them (<see cref="ScatterGatherClientStandIn.Hold"/>) and thrown
    /// when told to, then forwarded to the real client, which answers it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host registers no <see cref="IScatterGatherClient"/>.</exception>
    public static IServiceCollection HoldScatterGathers(
        this IServiceCollection services,
        ScatterGatherClientStandIn standIn)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(standIn);
        var client = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IScatterGatherClient))
            ?? throw new InvalidOperationException("The host registers no IScatterGatherClient: is AddScatterGather gone?");
        services.Remove(client);
        services.AddSingleton<IScatterGatherClient>(provider =>
        {
            standIn.ForwardTo(Resolve(provider, client));
            return standIn;
        });
        return services;
    }

    private static IScatterGatherClient Resolve(
        IServiceProvider provider,
        ServiceDescriptor client)
        => client switch
        {
            { ImplementationInstance: IScatterGatherClient instance } => instance,
            { ImplementationFactory: { } factory } => (IScatterGatherClient)factory(provider),
            { ImplementationType: { } type } => (IScatterGatherClient)ActivatorUtilities.CreateInstance(provider, type),
            _ => throw new InvalidOperationException("The host's IScatterGatherClient registration can't be resolved."),
        };
}
