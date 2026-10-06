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
            [Layers.Domain] = 0,
            [Layers.Application] = 1,
            // Persistence sits between Application and the outer adapters:
            // it implements Application's repository abstractions, and outer
            // adapters (notably Infrastructure for outbox / job wiring) may
            // legitimately depend on it. Putting Persistence at tier 2 makes
            // Infra/Presentation peers above it.
            [Layers.Persistence] = 2,
            [Layers.Infrastructure] = 3,
            [Layers.Presentation] = 3,
            [Layers.Web] = 4,
        };

    // Host should never reach into Domain directly; talk to Application instead.
    // Presentation should never know about Persistence; it only talks to
    // Application use cases. The tier rule alone allows the latter because
    // Presentation outranks Persistence; the extras restore strict separation.
    private static readonly (string Source, string Target)[] DefaultExtraForbidden =
    [
        (Layers.Web, Layers.Domain),
        (Layers.Presentation, Layers.Persistence),
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

    /// <summary>
    /// Each SharedKernel package → the layers that must not use it. An implementation package belongs to the outer
    /// layer that wires it; inner layers reach it through <c>Resrcify.SharedKernel.Abstractions</c>. Contracts,
    /// <c>Results</c> and <c>DomainDrivenDesign</c> are allowed everywhere. Override to loosen or tighten a package.
    /// </summary>
    protected virtual IReadOnlyDictionary<SharedKernelPackage, string[]> SharedKernelPackageRestrictions
        => DefaultSharedKernelPackageRestrictions;

    private static readonly string[] AllLayers =
        [Layers.Domain, Layers.Application, Layers.Persistence, Layers.Infrastructure, Layers.Presentation, Layers.Web];

    private static readonly IReadOnlyDictionary<SharedKernelPackage, string[]> DefaultSharedKernelPackageRestrictions
        = new Dictionary<SharedKernelPackage, string[]>
        {
            // The mediator is registered by Application; everything else sends through ISender/IPublisher.
            [SharedKernelPackage.Mediator] = [Layers.Domain, Layers.Persistence, Layers.Presentation],
            // The bus is wired by Infrastructure; handlers use IEventBus, IScatterGatherHandler, ….
            [SharedKernelPackage.MessageBus] = [Layers.Domain, Layers.Application, Layers.Persistence, Layers.Presentation],
            // The outbox and its interceptors belong where the DbContext and its jobs are.
            [SharedKernelPackage.UnitOfWork] = [Layers.Domain, Layers.Application, Layers.Presentation],
            // The PostgreSQL DbContext setup and its design-time factory: Persistence (and Infrastructure's wiring).
            [SharedKernelPackage.UnitOfWorkPostgres] = [Layers.Domain, Layers.Application, Layers.Presentation, Layers.Web],
            // Repository base classes are EF Core: Persistence only.
            [SharedKernelPackage.Repository] = [Layers.Domain, Layers.Application, Layers.Infrastructure, Layers.Presentation, Layers.Web],
            // Cache implementations; Application uses ICachingService.
            [SharedKernelPackage.Caching] = [Layers.Domain, Layers.Application, Layers.Persistence, Layers.Presentation],
            // ASP.NET Core helpers: the HTTP-facing layers (and Infrastructure's auth wiring).
            [SharedKernelPackage.Web] = [Layers.Domain, Layers.Application, Layers.Persistence],
            // Telemetry is wired by Infrastructure (AddServiceTelemetry) and the host (MapServiceMetrics).
            [SharedKernelPackage.Observability] = [Layers.Domain, Layers.Application, Layers.Persistence, Layers.Presentation],
            // Test helpers never ship in a layer.
            [SharedKernelPackage.ArchitectureTesting] = AllLayers,
            [SharedKernelPackage.IntegrationTesting] = AllLayers,
        };

    [SkippableFact]
    public virtual void Layers_ShouldNotDependOnSharedKernelPackagesOutsideTheirLayers()
    {
        var failures = new List<string>();

        foreach (var (package, layers) in SharedKernelPackageRestrictions)
        {
            foreach (var layer in layers)
            {
                var assembly = GetLayerAssembly(layer);
                if (assembly is null)
                    continue;

                var result = Types
                    .InAssembly(assembly)
                    .Should()
                    .NotHaveDependencyOn(package.Namespace())
                    .GetResult();

                if (!result.IsSuccessful && result.FailingTypeNames is { } failing)
                    failures.AddRange(failing.Select(typeName => $"{layer} → {package}: {typeName}"));
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
