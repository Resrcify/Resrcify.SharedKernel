using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Docker;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Factories;

/// <summary>
/// <c>OnHostStartedAsync</c>: run once, on the started host, before the first test; a failure in it, or in the host's
/// own start, tears down what the fixture had started.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ServiceHostFixtureHostStartedTests(
    HostStartedServiceHostFixture fixture)
    : IClassFixture<HostStartedServiceHostFixture>
{
    [Fact]
    public async Task OnHostStartedAsync_ShouldRunOnce_OnTheStartedHost_BeforeTheFirstTest()
    {
        fixture.Runs.ShouldBe(1);
        fixture.HostHadStarted.ShouldBeTrue();
        fixture.Greeting.ShouldBe("Hello from the service");
        fixture.TokenWasLive.ShouldBeTrue();

        using var client = fixture.CreateClient();
        (await client.GetStringAsync(new Uri("/greeting", UriKind.Relative))).ShouldBe("Hello from the service");
        _ = fixture.Services;

        fixture.Runs.ShouldBe(1);
    }

    [Fact]
    public async Task InitializeAsync_ShouldTearDownTheHostAndWhatItStarted_WhenOnHostStartedFails()
    {
        await using var recording = new RecordingServiceHostFixture(RecordingServiceHostFixture.Failure.OnHostStarted);
        IAsyncLifetime lifetime = recording;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(lifetime.InitializeAsync);

        thrown.Message.ShouldBe(RecordingServiceHostFixture.HostStartedFailure);
        recording.HostStartedRuns.ShouldBe(1);
        recording.Stopped.ShouldBe(["host", "last, container up", "first, container gone"]);
        (await DockerResources.ContainerExistsAsync(recording.DependencyId.ShouldNotBeNull())).ShouldBeFalse();
        (await DockerResources.NetworkExistsAsync(recording.NetworkName.ShouldNotBeNull())).ShouldBeFalse();

        await Should.NotThrowAsync(lifetime.DisposeAsync);
        recording.Stopped.ShouldBe(["host", "last, container up", "first, container gone"]);
    }

    [Fact]
    public async Task InitializeAsync_ShouldNotRunOnHostStarted_AndTearDownWhatItStarted_WhenTheHostFailsToStart()
    {
        await using var recording = new RecordingServiceHostFixture(RecordingServiceHostFixture.Failure.HostStart);
        IAsyncLifetime lifetime = recording;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(lifetime.InitializeAsync);

        thrown.Message.ShouldBe(RecordingServiceHostFixture.HostStartFailure);
        recording.HostStartedRuns.ShouldBe(0);
        recording.Stopped.TakeLast(2).ShouldBe(["last, container up", "first, container gone"]);
        (await DockerResources.ContainerExistsAsync(recording.DependencyId.ShouldNotBeNull())).ShouldBeFalse();
        (await DockerResources.NetworkExistsAsync(recording.NetworkName.ShouldNotBeNull())).ShouldBeFalse();

        await Should.NotThrowAsync(lifetime.DisposeAsync);
    }

    /// <summary>
    /// The order a loaded machine gave the 4.8.0 release run: the program's start fails and it disposes its host before
    /// <c>WebApplicationFactory</c> looks, which then throws <see cref="ObjectDisposedException"/> for the host's services.
    /// Held there deterministically by the fixture; the start's own failure must still be what is thrown.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ShouldThrowTheHostsStartFailure_WhenTheProgramDisposedTheHostBeforeTheFactoryLooked()
    {
        await using var recording = new RecordingServiceHostFixture(RecordingServiceHostFixture.Failure.HostStartSeenLate);
        IAsyncLifetime lifetime = recording;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(lifetime.InitializeAsync);

        thrown.Message.ShouldBe(RecordingServiceHostFixture.HostStartFailure);
        recording.HostStartedRuns.ShouldBe(0);
        recording.Stopped.TakeLast(2).ShouldBe(["last, container up", "first, container gone"]);
        (await DockerResources.ContainerExistsAsync(recording.DependencyId.ShouldNotBeNull())).ShouldBeFalse();
        (await DockerResources.NetworkExistsAsync(recording.NetworkName.ShouldNotBeNull())).ShouldBeFalse();

        await Should.NotThrowAsync(lifetime.DisposeAsync);
    }
}
