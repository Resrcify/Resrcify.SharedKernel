using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.ArchitectureTesting.Layering;
using Resrcify.SharedKernel.ArchitectureTesting.PackageRules;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.PackageRules;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PackageRestrictionInheritanceTests
{
    private static readonly string[] Conventional =
        [Layers.Domain, Layers.Application, Layers.Persistence, Layers.Infrastructure, Layers.Presentation, Layers.Web];

    // A slice of the conventional restrictions: Web may not use Repository (nor may Infrastructure and Presentation);
    // Infrastructure may use Mediator, Presentation may not; both may use the Web package.
    private static readonly Dictionary<SharedKernelPackage, string[]> Restrictions = new()
    {
        [SharedKernelPackage.Mediator] = [Layers.Domain, Layers.Persistence, Layers.Presentation],
        [SharedKernelPackage.Repository] = [Layers.Domain, Layers.Application, Layers.Infrastructure, Layers.Presentation, Layers.Web],
        [SharedKernelPackage.Web] = [Layers.Domain, Layers.Application, Layers.Persistence],
    };

    private static readonly Dictionary<string, int> ConventionalTiers = new(StringComparer.Ordinal)
    {
        [Layers.Domain] = 0,
        [Layers.Application] = 1,
        [Layers.Persistence] = 2,
        [Layers.Infrastructure] = 3,
        [Layers.Presentation] = 3,
        [Layers.Web] = 4,
    };

    [Fact]
    public void Resolve_ShouldRestrictACustomLayerExactlyLikeWeb_WhenItSitsAtWebsTier()
    {
        var resolved = Resolve(TiersWith("Bot", 4));

        resolved.Restrictions[SharedKernelPackage.Repository].ShouldContain("Bot");
        resolved.Restrictions[SharedKernelPackage.Mediator].ShouldNotContain("Bot");
        resolved.Restrictions[SharedKernelPackage.Web].ShouldNotContain("Bot");
        resolved.InheritedFrom["Bot"].ShouldBe([Layers.Web]);
    }

    [Fact]
    public void Resolve_ShouldRestrictACustomLayerLikeEveryLayerAtItsTier_WhenSeveralShareIt()
    {
        var resolved = Resolve(TiersWith("Client", 3));

        resolved.Restrictions[SharedKernelPackage.Mediator].ShouldContain("Client");      // like Presentation
        resolved.Restrictions[SharedKernelPackage.Repository].ShouldContain("Client");    // like both
        resolved.Restrictions[SharedKernelPackage.Web].ShouldNotContain("Client");        // neither
        resolved.InheritedFrom["Client"].ShouldBe([Layers.Infrastructure, Layers.Presentation]);
    }

    [Theory]
    [InlineData(9, Layers.Web)]
    [InlineData(-1, Layers.Domain)]
    public void Resolve_ShouldRestrictACustomLayerLikeTheNearestTier_WhenNoLayerSharesItsTier(int tier, string expected)
        => Resolve(TiersWith("Host", tier)).InheritedFrom["Host"].ShouldBe([expected]);

    [Fact]
    public void Resolve_ShouldTakeTheInnerTier_WhenTwoTiersAreAsNear()
    {
        var tiers = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Layers.Domain] = 0,
            [Layers.Web] = 4,
            ["Middle"] = 2,
        };

        Resolve(tiers).InheritedFrom["Middle"].ShouldBe([Layers.Domain]);
    }

    [Fact]
    public void Resolve_ShouldFollowPackageRestrictionsLike_InsteadOfTheTier()
    {
        var resolved = Resolve(
            TiersWith("Bot", 4),
            like: new Dictionary<string, string> { ["Bot"] = Layers.Persistence });

        resolved.Restrictions[SharedKernelPackage.Mediator].ShouldContain("Bot");
        resolved.Restrictions[SharedKernelPackage.Web].ShouldContain("Bot");
        resolved.Restrictions[SharedKernelPackage.Repository].ShouldNotContain("Bot");
        resolved.DescribeOrigin("Bot").ShouldContain("restricted like Persistence");
    }

    [Fact]
    public void Resolve_ShouldLeaveACustomLayerAsConfigured_WhenTheRestrictionsNameItAlready()
    {
        // Discord's 4.0.0 workaround: Bot added wherever Web is restricted.
        var restrictions = new Dictionary<SharedKernelPackage, string[]>(Restrictions)
        {
            [SharedKernelPackage.Repository] = [.. Restrictions[SharedKernelPackage.Repository], "Bot"],
        };

        var resolved = PackageRestrictionInheritance.Resolve(
            restrictions,
            TiersWith("Bot", 4),
            new Dictionary<string, string>(),
            Conventional);

        resolved.Restrictions[SharedKernelPackage.Repository].ShouldBe(restrictions[SharedKernelPackage.Repository]);
        resolved.InheritedFrom.ShouldNotContainKey("Bot");
        resolved.DescribeOrigin("Bot").ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_ShouldNotRestrictAConventionalLayerAgain_WhenAServiceLoosenedItEverywhere()
    {
        var loosened = new Dictionary<SharedKernelPackage, string[]>
        {
            [SharedKernelPackage.Repository] = [Layers.Domain],
        };

        var resolved = PackageRestrictionInheritance.Resolve(
            loosened,
            ConventionalTiers,
            new Dictionary<string, string>(),
            Conventional);

        resolved.Restrictions[SharedKernelPackage.Repository].ShouldBe([Layers.Domain]);
        resolved.InheritedFrom.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Bot", "Bot")]
    [InlineData("Bot", "Wbe")]
    public void Resolve_ShouldThrow_WhenPackageRestrictionsLikeNamesNoLayerToTakeFrom(string layer, string like)
        => Should.Throw<InvalidOperationException>(
                () => Resolve(TiersWith(layer, 4), like: new Dictionary<string, string> { [layer] = like }))
            .Message.ShouldContain("PackageRestrictionsLike");

    private static PackageRestrictionInheritance Resolve(
        Dictionary<string, int> tiers,
        Dictionary<string, string>? like = null)
        => PackageRestrictionInheritance.Resolve(
            Restrictions,
            tiers,
            like ?? new Dictionary<string, string>(),
            Conventional);

    private static Dictionary<string, int> TiersWith(
        string layer,
        int tier)
        => new(ConventionalTiers, StringComparer.Ordinal) { [layer] = tier };
}
