using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Resrcify.SharedKernel.ArchitectureTesting.Helpers;
using Resrcify.SharedKernel.ArchitectureTesting.Tests;
using Resrcify.SharedKernel.Repository.Primitives;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// The package rule on a service with a custom layer: a <c>Bot</c> host at Web's tier, as Discord has. The Repository
/// package's own assembly stands in for a Bot that uses the Repository package (which Web may not); the Results package's
/// for one that uses nothing restricted.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ConventionalLayerDependencyTestsTests
{
    private const string Bot = nameof(Bot);

    private static Assembly UsesRepository
        => typeof(Specification<,>).Assembly;

    private static Assembly UsesNothingRestricted
        => typeof(Result).Assembly;

    [Fact]
    public void PackageRule_ShouldFail_WhenACustomLayerAtWebsTierUsesAPackageWebMayNotUse()
    {
        var rules = new BotLayerTests(UsesRepository);

        var failure = Should.Throw<ShouldAssertException>(rules.Layers_ShouldNotDependOnSharedKernelPackagesOutsideTheirLayers);

        failure.Message.ShouldContain($"{Bot} → {SharedKernelPackage.Repository}: ");
        failure.Message.ShouldContain("restricted like Web");
    }

    [Fact]
    public void PackageRule_ShouldPass_WhenTheCustomLayerUsesNoRestrictedPackage()
        => new BotLayerTests(UsesNothingRestricted).Layers_ShouldNotDependOnSharedKernelPackagesOutsideTheirLayers();

    [Fact]
    public void PackageRule_ShouldRestrictTheCustomLayerLikeTheLayerNamed_WhenPackageRestrictionsLikeMapsIt()
    {
        // Persistence may use the Repository package, so a Bot restricted like Persistence may too.
        var rules = new BotLayerTests(UsesRepository, restrictLike: Layers.Persistence);

        rules.Layers_ShouldNotDependOnSharedKernelPackagesOutsideTheirLayers();
    }

    [Fact]
    public void PackageRule_ShouldNameTheMappedLayer_WhenTheCustomLayerBreaksItsRestrictions()
    {
        var rules = new BotLayerTests(UsesRepository, restrictLike: Layers.Infrastructure);

        Should.Throw<ShouldAssertException>(rules.Layers_ShouldNotDependOnSharedKernelPackagesOutsideTheirLayers)
            .Message.ShouldContain("restricted like Infrastructure");
    }

    private sealed class BotLayerTests(
        Assembly botAssembly,
        string? restrictLike = null)
        : ConventionalLayerDependencyTests(new Dictionary<string, Assembly> { [Bot] = botAssembly })
    {
        protected override IReadOnlyDictionary<string, int> LayerTiers
            => new Dictionary<string, int>(base.LayerTiers) { [Bot] = base.LayerTiers[Layers.Web] };

        protected override IReadOnlyDictionary<string, string> PackageRestrictionsLike
            => restrictLike is null
                ? base.PackageRestrictionsLike
                : new Dictionary<string, string> { [Bot] = restrictLike };
    }
}
