using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>Lets the message bus find its handlers in the assemblies the mediator scans.</summary>
internal static class MediatorScans
{
    /// <summary>
    /// Hands <paramref name="listener"/> every assembly the mediator scans: those of <c>AddMediator</c> calls made so
    /// far now, and those of later calls as they happen, so <c>AddMediator</c> and <c>AddMessageBus</c> can come in
    /// any order. Following with the same listener again adds nothing.
    /// </summary>
    internal static void FollowMediatorScans(this IServiceCollection services, IMediatorAssemblyScanListener listener)
    {
        var alreadyFollowing = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IMediatorAssemblyScanListener) &&
            ReferenceEquals(descriptor.ImplementationInstance, listener));
        if (alreadyFollowing)
            return;

        var earlierScans = services
            .Where(descriptor => descriptor.ServiceType == typeof(IMediatorAssemblyScan))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IMediatorAssemblyScan>()
            .ToList();
        foreach (var scan in earlierScans)
            listener.OnScanned(services, scan.Assemblies);

        services.AddSingleton(listener);
    }
}
