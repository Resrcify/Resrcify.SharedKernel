using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ContainerFixtureTests
{
    [Fact]
    public async Task DisposeAsync_ShouldDoNothing_WhenTheContainerWasNeverBuilt()
    {
        var fixture = new UnbuildableFixture();

        await Should.ThrowAsync<ArgumentException>(fixture.InitializeAsync);

        // It used to throw a NullReferenceException here, hiding the real error and skipping the rest of a teardown.
        await Should.NotThrowAsync(fixture.DisposeAsync);
    }

    /// <summary>A fixture whose container can't be built, as with an empty image name.</summary>
    private sealed class UnbuildableFixture : ContainerFixture<IContainer>
    {
        protected override IContainer Build()
            => throw new ArgumentException("'image' cannot be empty.");
    }
}
