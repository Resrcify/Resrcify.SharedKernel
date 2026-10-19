using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Resrcify.SharedKernel.IntegrationTesting.Containers;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Containers;

/// <summary>
/// On Docker, with the image the PostgreSQL fixture already runs (a local copy, so nothing is pulled from a registry):
/// the first start says what ran, and names the build by its registry digest.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ImageUnderTestTests
{
    private const string Image = "postgres:18";

    [Fact]
    public async Task StartAsync_ShouldDescribeTheBuildItRan_ByItsRegistryDigest()
    {
        var image = new ImageUnderTest("Postgres", Image, "the fixtures' image", ImageAcquisition.LocalOnly, "set the image");

        await using var container = await image.StartAsync(builder => builder.WithEnvironment("POSTGRES_PASSWORD", "test"));

        image.Description.ShouldStartWith($"Postgres: {Image} (the fixtures' image; local copy, not pulled fresh) = version ");
        image.Description.ShouldContain("postgres@sha256:");
        image.Description.ShouldContain(", image sha256:");
    }
}
