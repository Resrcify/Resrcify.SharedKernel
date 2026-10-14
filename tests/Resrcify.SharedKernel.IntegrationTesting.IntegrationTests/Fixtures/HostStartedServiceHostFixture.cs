using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.IntegrationTesting.Factories;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.TestService;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>
/// A service without a database whose <c>OnHostStartedAsync</c> notes what it found: how many times it ran, whether the
/// host had started, and a service it resolved.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public")]
public sealed class HostStartedServiceHostFixture
    : ServiceHostFixture<Program>
{
    private int _runs;

    /// <summary>How many times <c>OnHostStartedAsync</c> ran.</summary>
    public int Runs
        => Volatile.Read(ref _runs);

    /// <summary>Whether the host had started (<c>ApplicationStarted</c>) when it ran.</summary>
    public bool HostHadStarted { get; private set; }

    /// <summary>What the service's <see cref="IGreeter"/> answered when it ran.</summary>
    public string? Greeting { get; private set; }

    /// <summary>Whether the token it was given was still live.</summary>
    public bool TokenWasLive { get; private set; }

    protected override PostgresContainerFixture? CreatePostgres()
        => null;

    protected override Task OnHostStartedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runs);
        HostHadStarted = services
            .GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStarted
            .IsCancellationRequested;
        Greeting = services.GetRequiredService<IGreeter>().Greet();
        TokenWasLive = cancellationToken.CanBeCanceled && !cancellationToken.IsCancellationRequested;
        return Task.CompletedTask;
    }
}
