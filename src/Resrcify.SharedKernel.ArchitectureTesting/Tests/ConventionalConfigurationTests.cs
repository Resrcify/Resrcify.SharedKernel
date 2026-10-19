using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Resrcify.SharedKernel.ArchitectureTesting.Layering;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.Tests;

/// <summary>
/// Configuration is read at registration only (<c>Program</c> and the <c>Add*</c> registration extensions) and reaches
/// services as validated options (<c>IOptions&lt;T&gt;</c>): no type takes <c>IConfiguration</c> in a constructor.
/// </summary>
/// <remarks>
/// Constructors only, in every layer assembly: a registration extension method that takes <c>IConfiguration</c>
/// (<c>AddInfrastructure(this IServiceCollection services, IConfiguration configuration)</c>) is where configuration
/// belongs, and isn't checked. <c>IConfiguration</c> is matched by its full name (also anything implementing it:
/// <c>IConfigurationSection</c>, <c>IConfigurationRoot</c>, <c>ConfigurationManager</c>), so this package needs no
/// reference to it. Every test is <c>virtual</c>: override and skip with the reason to opt out.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public abstract bases via inheritance.")]
public abstract class ConventionalConfigurationTests : BaseArchitectureTest
{
    private const string ConfigurationFullName = "Microsoft.Extensions.Configuration.IConfiguration";

    /// <summary>A top-level program's generated class: it registers services, so it may read configuration.</summary>
    private const string ProgramTypeName = "Program";

    /// <summary>Checks the layer assemblies found by naming convention (see <see cref="LayerAssemblyResolver"/>).</summary>
    protected ConventionalConfigurationTests()
    {
    }

    /// <summary>
    /// Checks the given layer assemblies (layer name → assembly) instead of the ones found by naming convention.
    /// </summary>
    protected ConventionalConfigurationTests(IReadOnlyDictionary<string, Assembly> layerAssemblies)
        : base(layerAssemblies)
    {
    }

    /// <summary>
    /// No class (but <c>Program</c>) has a constructor, of any visibility, with an <c>IConfiguration</c> parameter: a
    /// service takes the options it needs (<c>IOptions&lt;T&gt;</c>, bound and validated at registration) instead.
    /// </summary>
    [SkippableFact]
    public virtual void Types_Should_NotTakeIConfigurationInAConstructor()
    {
        Skip.If(LayerAssemblies.Count == 0);

        var failing = new List<string>();
        foreach (var (layer, assembly) in LayerAssemblies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var types = Types
                .InAssembly(assembly)
                .That()
                .AreClasses()
                .GetTypes()
                .Where(type => !string.Equals(type.Name, ProgramTypeName, StringComparison.Ordinal));

            foreach (var type in types)
            {
                failing.AddRange(ConfigurationParameters(type)
                    .Select(parameter => $"{layer}: {type.FullName} → constructor parameter '{parameter.Name}' ({parameter.ParameterType.Name})"));
            }
        }

        failing.ShouldBeEmpty();
    }

    private static IEnumerable<ParameterInfo> ConfigurationParameters(Type type)
        => type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => IsConfiguration(parameter.ParameterType));

    private static bool IsConfiguration(Type type)
        => string.Equals(type.FullName, ConfigurationFullName, StringComparison.Ordinal)
            || type.GetInterfaces().Any(contract => string.Equals(contract.FullName, ConfigurationFullName, StringComparison.Ordinal));
}
