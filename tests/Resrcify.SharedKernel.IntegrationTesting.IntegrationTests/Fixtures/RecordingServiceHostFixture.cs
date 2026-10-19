using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.IntegrationTesting.Factories;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Docker;
using Testcontainers.PostgreSql;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>
/// A fixture a test starts and tears down itself, recording the order things stop in: the host, and what
/// <c>StartDependenciesAsync</c> handed to <c>DisposeWithFixture</c>: a real container (an <c>IAsyncDisposable</c>)
/// between two markers (<c>IAsyncLifetime</c>) that note, as they are disposed, whether the container is still there.
/// Optionally it fails after them, as the host starts, or in <c>OnHostStartedAsync</c>; it counts the runs of
/// <c>OnHostStartedAsync</c>.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Used by public test classes")]
public sealed class RecordingServiceHostFixture(
    RecordingServiceHostFixture.Failure failure)
    : ServiceHostFixture<Program>
{
    public const string HookFailure = "The dependency failed to start.";
    public const string HostStartFailure = "A hosted service failed to start.";
    public const string HostStartedFailure = "Getting the host ready failed.";

    private readonly List<string> _stopped = [];
    private int _hostStartedRuns;

    /// <summary>Where the fixture fails, if anywhere.</summary>
    public enum Failure
    {
        None,
        StartDependencies,
        HostStart,
        OnHostStarted,
    }

    /// <summary>How many times <c>OnHostStartedAsync</c> ran.</summary>
    public int HostStartedRuns
        => Volatile.Read(ref _hostStartedRuns);

    /// <summary>The container started in <c>StartDependenciesAsync</c>.</summary>
    public IContainer? Dependency { get; private set; }

    /// <summary>Its id, kept: Testcontainers forgets it once the container is disposed.</summary>
    public string? DependencyId { get; private set; }

    /// <summary>The network's name, kept for the same reason.</summary>
    public string? NetworkName { get; private set; }

    /// <summary>What stopped, in order.</summary>
    public IReadOnlyList<string> Stopped
    {
        get
        {
            lock (_stopped)
                return [.. _stopped];
        }
    }

    protected override bool UsesNetwork
        => true;

    protected override PostgresContainerFixture? CreatePostgres()
        => null;

    protected override async Task StartDependenciesAsync(CancellationToken cancellationToken)
    {
        NetworkName = Network!.Name;
        DisposeWithFixture(new Marker("first", this));
        Dependency = DisposeWithFixture(new PostgreSqlBuilder("postgres:18")
            .WithNetwork(Network)
            .WithNetworkAliases("dependency")
            .Build());
        await Dependency.StartAsync(cancellationToken);
        DependencyId = Dependency.Id;
        DisposeWithFixture(new Marker("last", this));

        if (failure == Failure.StartDependencies)
            throw new InvalidOperationException(HookFailure);
    }

    protected override Task OnHostStartedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _hostStartedRuns);
        if (failure == Failure.OnHostStarted)
            throw new InvalidOperationException(HostStartedFailure);
        return Task.CompletedTask;
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddHostedService(_ => new HostStop(this));
        if (failure == Failure.HostStart)
            services.AddHostedService<FailingStart>();
    }

    private void Record(string name)
    {
        lock (_stopped)
            _stopped.Add(name);
    }

    private sealed class Marker(
        string name,
        RecordingServiceHostFixture fixture)
        : IAsyncLifetime
    {
        public Task InitializeAsync()
            => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            var container = await DockerResources.ContainerExistsAsync(fixture.DependencyId!)
                ? "container up"
                : "container gone";
            fixture.Record($"{name}, {container}");
        }
    }

    private sealed class FailingStart
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException(HostStartFailure);

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    // Noted once: the factory may stop the host more than once.
    private sealed class HostStop(
        RecordingServiceHostFixture fixture)
        : IHostedService
    {
        private int _stopped;

        public Task StartAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
                fixture.Record("host");
            return Task.CompletedTask;
        }
    }
}
