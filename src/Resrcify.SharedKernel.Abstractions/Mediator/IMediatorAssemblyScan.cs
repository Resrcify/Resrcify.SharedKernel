using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.Abstractions.Mediator;

/// <summary>
/// The assemblies one <c>AddMediator</c> call scanned, recorded in the service collection so other packages
/// can find handlers of their own in the same assemblies (the message bus finds scatter-gather handlers
/// this way). Read it during registration, from the <see cref="IServiceCollection"/>'s descriptors.
/// </summary>
public interface IMediatorAssemblyScan
{
    IReadOnlyCollection<Assembly> Assemblies { get; }
}

/// <summary>
/// Told about every <c>AddMediator</c> scan made after it was registered, so a package can follow the
/// mediator's scan whichever of the two is registered first. Register an instance as a singleton; to
/// cover scans made before that, also read the <see cref="IMediatorAssemblyScan"/>s already registered.
/// </summary>
public interface IMediatorAssemblyScanListener
{
    void OnScanned(IServiceCollection services, IReadOnlyCollection<Assembly> assemblies);
}
