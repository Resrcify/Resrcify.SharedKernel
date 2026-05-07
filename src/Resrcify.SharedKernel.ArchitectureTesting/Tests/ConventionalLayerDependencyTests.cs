using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NetArchTest.Rules;
using Resrcify.SharedKernel.ArchitectureTesting.Helpers;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.Tests;

/// <summary>
/// Asserts the layered Clean Architecture dependency direction. Rules are
/// derived from a tier model rather than a hand-maintained pairwise matrix:
/// <list type="bullet">
///   <item>Lower-tier layers must not depend on higher-tier layers (Clean
///   Architecture's "dependencies point inward").</item>
///   <item>Same-tier layers are peers and must not depend on each other.</item>
///   <item>Extra-strict edges the tier model can't express
///   (e.g. <c>Web/Bot → Domain</c>) live in <see cref="ExtraForbiddenDependencies"/>.</item>
/// </list>
///
/// Override <see cref="LayerTiers"/> to add custom layers (Bot,
/// SourceGenerators, Client, …) or change tier assignments. Override
/// <see cref="ExtraForbiddenDependencies"/> to add bespoke rules. Layers
/// absent from the consumer's solution are silently skipped.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public abstract bases via inheritance.")]
public abstract class ConventionalLayerDependencyTests : BaseArchitectureTest
{
    /// <summary>
    /// Layer → tier map. Lower tier is more inner / core. Same-tier layers are
    /// peers. Override to extend with custom layers or re-tier existing ones.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, int> LayerTiers
        => DefaultLayerTiers;

    /// <summary>
    /// Extra-strict edges beyond the tier rules. The default forbids
    /// <c>Web → Domain</c> so host layers go through Application
    /// contracts instead of touching Domain directly.
    /// </summary>
    protected virtual IEnumerable<(string Source, string Target)> ExtraForbiddenDependencies
        => DefaultExtraForbidden;

    private static readonly IReadOnlyDictionary<string, int> DefaultLayerTiers
        = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Domain"] = 0,
            ["Application"] = 1,
            // Persistence sits between Application and the outer adapters:
            // it implements Application's repository abstractions, and outer
            // adapters (notably Infrastructure for outbox / job wiring) may
            // legitimately depend on it. Putting Persistence at tier 2 makes
            // Infra/Presentation peers above it.
            ["Persistence"] = 2,
            ["Infrastructure"] = 3,
            ["Presentation"] = 3,
            ["Web"] = 4,
        };

    // Host should never reach into Domain directly; talk to Application instead.
    // Presentation should never know about Persistence; it only talks to
    // Application use cases. The tier rule alone allows the latter because
    // Presentation outranks Persistence; the extras restore strict separation.
    private static readonly (string Source, string Target)[] DefaultExtraForbidden =
    [
        ("Web", "Domain"),
        ("Presentation", "Persistence"),
    ];

    [SkippableFact]
    public virtual void Layers_ShouldNotDependOnForbiddenLayers()
    {
        var forbidden = DeriveTierEdges(LayerTiers)
            .Concat(ExtraForbiddenDependencies)
            .Distinct()
            .ToList();

        var failures = new List<string>();

        foreach (var (source, target) in forbidden)
        {
            var srcAsm = GetLayerAssembly(source);
            var tgtAsm = GetLayerAssembly(target);
            if (srcAsm is null || tgtAsm is null)
                continue;

            var result = Types
                .InAssembly(srcAsm)
                .Should()
                .NotHaveDependencyOn(tgtAsm.GetName().Name!)
                .GetResult();

            if (!result.IsSuccessful && result.FailingTypeNames is { } failing)
            {
                foreach (var typeName in failing)
                    failures.Add($"{source} → {target}: {typeName}");
            }
        }

        failures.ShouldBeEmpty();
    }

    private static IEnumerable<(string Source, string Target)> DeriveTierEdges(
        IReadOnlyDictionary<string, int> tiers)
    {
        foreach (var src in tiers)
        {
            foreach (var dst in tiers)
            {
                if (StringComparer.Ordinal.Equals(src.Key, dst.Key))
                    continue;

                // Forbidden if src tier <= dst tier:
                //   src tier <  dst tier  →  inner depends on outer (Clean violation)
                //   src tier == dst tier  →  peer depends on peer
                if (src.Value <= dst.Value)
                    yield return (src.Key, dst.Key);
            }
        }
    }
}
