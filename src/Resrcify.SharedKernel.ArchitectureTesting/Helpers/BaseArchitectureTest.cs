using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.Helpers;

/// <summary>
/// Base for any architecture test class that needs access to the consumer
/// project's layer assemblies. Subclasses get <see cref="LayerAssemblies"/>
/// resolved automatically from the consumer's test assembly via
/// <see cref="LayerAssemblyResolver"/>.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Base class is consumed cross-assembly by xUnit test classes.")]
public abstract class BaseArchitectureTest
{
    protected IReadOnlyDictionary<string, Assembly> LayerAssemblies { get; }

    protected BaseArchitectureTest()
        => LayerAssemblies = LayerAssemblyResolver.Resolve(GetType().Assembly);

    protected Assembly? GetLayerAssembly(string layer)
        => LayerAssemblies.TryGetValue(layer, out var asm) ? asm : null;

    protected void SkipIfNoAssembly(string layer)
        => Skip.If(GetLayerAssembly(layer) is null);
}
