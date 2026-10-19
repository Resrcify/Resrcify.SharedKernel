using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.IntegrationTesting.Containers;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.UnitTests.Containers;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ImageReferencesTests
{
    [Theory]
    [InlineData("ghcr.io/acme/orders:latest", "ghcr.io/acme/orders")]
    [InlineData("ghcr.io/acme/orders", "ghcr.io/acme/orders")]
    [InlineData("ghcr.io/acme/orders@sha256:abc", "ghcr.io/acme/orders")]
    [InlineData("ghcr.io/acme/orders:1.2@sha256:abc", "ghcr.io/acme/orders:1.2")]
    [InlineData("localhost:5000/orders", "localhost:5000/orders")]
    [InlineData("localhost:5000/orders:1", "localhost:5000/orders")]
    [InlineData("postgres:18", "postgres")]
    public void Repository_ShouldDropTheTagOrDigest_ButNotARegistryPort(string reference, string repository)
        => ImageReferences.Repository(reference).ShouldBe(repository);

    [Theory]
    [InlineData("ghcr.io/acme/orders:latest", "ghcr.io")]
    [InlineData("localhost:5000/orders", "localhost:5000")]
    [InlineData("localhost/orders", "localhost")]
    [InlineData("acme/orders:1", "docker.io")]
    [InlineData("postgres:18", "docker.io")]
    public void Registry_ShouldBeTheHostTheReferenceNames_OrDockerHub(string reference, string registry)
        => ImageReferences.Registry(reference).ShouldBe(registry);

    [Theory]
    [InlineData("ghcr.io/acme/orders@sha256:abc", ImageAcquisition.PullFresh, "pinned by digest: exactly this build")]
    [InlineData("ghcr.io/acme/orders@sha256:abc", ImageAcquisition.LocalOnly, "pinned by digest: exactly this build")]
    [InlineData("ghcr.io/acme/orders:latest", ImageAcquisition.PullFresh, "pulled fresh")]
    [InlineData("ghcr.io/acme/orders:latest", ImageAcquisition.LocalOnly, "local copy, not pulled fresh")]
    public void Obtained_ShouldSayHowTheRunGotTheImage(string reference, ImageAcquisition acquisition, string obtained)
        => ImageReferences.Obtained(reference, acquisition).ShouldBe(obtained);

    [Fact]
    public void Description_ShouldNameTheRequestedImage_BeforeAContainerStarted()
        => new ImageUnderTest("Orders", "ghcr.io/acme/orders:latest", "the default", ImageAcquisition.PullFresh, "set ORDERS_IMAGE")
            .Description.ShouldBe("Orders: ghcr.io/acme/orders:latest (the default; not started yet)");
}
