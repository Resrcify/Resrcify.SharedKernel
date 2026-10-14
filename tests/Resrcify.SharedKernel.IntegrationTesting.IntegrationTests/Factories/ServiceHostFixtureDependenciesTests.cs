using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Support;
using Resrcify.SharedKernel.IntegrationTesting.TestService;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Factories;

/// <summary>
/// A service without the fixture's PostgreSQL that starts a container of its own in <c>StartDependenciesAsync</c>, and
/// the order a fixture tears that down in, after a run and after a failed start.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ServiceHostFixtureDependenciesTests(
    DependencyServiceHostFixture fixture)
    : IClassFixture<DependencyServiceHostFixture>
{
    [Fact]
    public async Task StartDependenciesAsync_ShouldStartAContainer_WhoseAddressConfigureSettingsGivesTheHost()
    {
        fixture.NetworkWasUpInTheHook.ShouldBeTrue();
        fixture.Settings["Database:Host"].ShouldBe(fixture.Dependency.Host);
        fixture.Settings["Database:Port"].ShouldBe(fixture.Dependency.Port);

        using var client = fixture.CreateClient();

        (await client.GetStringAsync(new Uri("/database", UriKind.Relative)))
            .ShouldBe(DependencyServiceHostFixture.DependencyDatabase);
        fixture.Services.GetRequiredService<RegistrationSettings>().Values["Database:Port"]
            .ShouldBe(fixture.Dependency.Port);
    }

    [Fact]
    public async Task StartDependenciesAsync_ShouldStartTheContainerOnTheFixturesNetwork()
    {
        var resolved = await fixture.Dependency.Container.ExecAsync(
            ["getent", "hosts", DependencyServiceHostFixture.DependencyAlias]);

        resolved.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Postgres_ShouldBeNone_WhenCreatePostgresReturnsNull()
    {
        Should.Throw<InvalidOperationException>(() => fixture.Postgres);
        fixture.Settings.Keys.ShouldNotContain("Migrations:Run");
        fixture.RabbitMq.ShouldBeNull();
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopTheHost_ThenDisposeTheDependenciesLastFirst_ThenDeleteTheNetwork()
    {
        await using var recording = new RecordingServiceHostFixture(failInTheHook: false);
        IAsyncLifetime lifetime = recording;
        await lifetime.InitializeAsync();
        var container = recording.DependencyId.ShouldNotBeNull();
        var network = recording.NetworkName.ShouldNotBeNull();
        using (var client = recording.CreateClient())
            (await client.GetStringAsync(new Uri("/greeting", UriKind.Relative))).ShouldNotBeEmpty();

        await lifetime.DisposeAsync();

        recording.Stopped.ShouldBe(["host", "last, container up", "first, container gone"]);
        (await DockerResources.ContainerExistsAsync(container)).ShouldBeFalse();
        (await DockerResources.NetworkExistsAsync(network)).ShouldBeFalse();
    }

    [Fact]
    public async Task InitializeAsync_ShouldTearDownWhatItStarted_WhenStartDependenciesFails()
    {
        await using var recording = new RecordingServiceHostFixture(failInTheHook: true);
        IAsyncLifetime lifetime = recording;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(lifetime.InitializeAsync);

        thrown.Message.ShouldBe(RecordingServiceHostFixture.HookFailure);
        recording.Stopped.ShouldBe(["last, container up", "first, container gone"]);
        (await DockerResources.ContainerExistsAsync(recording.DependencyId.ShouldNotBeNull())).ShouldBeFalse();
        (await DockerResources.NetworkExistsAsync(recording.NetworkName.ShouldNotBeNull())).ShouldBeFalse();

        // xUnit still disposes a fixture whose start failed: nothing is left to do, and nothing is done twice.
        await Should.NotThrowAsync(lifetime.DisposeAsync);
        recording.Stopped.ShouldBe(["last, container up", "first, container gone"]);
    }
}
