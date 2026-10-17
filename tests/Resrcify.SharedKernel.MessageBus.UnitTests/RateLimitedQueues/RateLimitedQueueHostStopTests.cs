using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

/// <summary>
/// A rate-limited queue's bus is stopped once, however many callers stop it at once, and none is left running after the
/// host stopped. Each test puts its callers where it needs them with <see cref="HeldBusStrategy"/> (a held start or
/// disposal), never with timing, and repeats the scenario on fresh hosts.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RateLimitedQueueHostStopTests
{
    private const string StoppedLine = "Stopped consuming";
    private const string StartedLine = "Consuming";
    private const string HealthTag = "upstream";

    /// <summary>The scenarios run this often, each on a fresh host.</summary>
    private const int Rounds = 25;

    /// <summary>A bound for waits that only a failed test reaches.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task StopAsync_ShouldStopTheQueueOnce_WhenTheHostIsStoppedTwiceAtOnce()
    {
        for (var round = 0; round < Rounds; round++)
            await StopTheHostTwiceAtOnceAsync();
    }

    [Fact]
    public async Task Dispose_ShouldNotStopTheQueueAgain_WhileStopAsyncIsStoppingIt()
    {
        for (var round = 0; round < Rounds; round++)
            await DisposeWhileStoppingAsync();
    }

    [Fact]
    public async Task Dispose_ShouldLeaveNoBusRunning_WhenTheHealthGateIsRestartingTheQueue()
    {
        for (var round = 0; round < Rounds; round++)
            await StopWhileRestartingAsync(HostStop.Dispose);
    }

    [Fact]
    public async Task StopAsync_ShouldLeaveNoBusRunning_WhenItStopsPastItsShutdownTimeoutWhileTheHealthGateRestartsTheQueue()
    {
        for (var round = 0; round < Rounds; round++)
            await StopWhileRestartingAsync(HostStop.StopAsyncPastItsShutdownTimeout);
    }

    /// <summary>
    /// What a test host does to a service: <c>WebApplicationFactory.DisposeAsync</c> stops the host, which wakes
    /// <c>Program.cs</c>'s <c>app.RunAsync()</c> (waiting in <c>WaitForShutdownAsync</c>), and that stops the host too.
    /// <c>Host.StopAsync</c> has no guard against a second call, so each hosted service is stopped twice at once. The
    /// duplicate came from the second stop arriving while the first was disposing the queue's bus (which waits for its
    /// workers): here the second stop starts once the first holds that disposal.
    /// </summary>
    private static async Task StopTheHostTwiceAtOnceAsync()
    {
        var queue = $"stopped-twice-{Guid.NewGuid():N}";
        using var strategy = new HeldBusStrategy(address => address == queue);
        using var logs = new FakeLoggerProvider();
        using var host = await StartAsync(queue, strategy, logs);
        var consumer = Consumer(host, queue);
        await InMemoryServices.WaitUntilAsync(() => consumer.IsConsuming);
        strategy.HoldNextDisposal();

        // The factory's stop, on a thread of its own: holding the queue's disposal holds its caller's thread.
        var factoryStop = Task.Run(() => host.StopAsync());
        try
        {
            await strategy.DisposalHeld.WaitAsync(GiveUpAfter);
            // The program's stop, while the factory's disposes the bus: it has nothing left to stop, so it finishes.
            await StopAgainAsync(host).WaitAsync(GiveUpAfter);
            Lines(logs, StoppedLine).ShouldBe(0);
        }
        finally
        {
            strategy.ReleaseDisposal();
            await factoryStop;
        }

        Lines(logs, StoppedLine).ShouldBe(1);
        strategy.Disposed.ShouldBe(1);
        consumer.IsConsuming.ShouldBeFalse();
    }

    /// <summary><c>app.RunAsync()</c>'s stop, once <c>WaitForShutdownAsync</c> has seen the host stopping.</summary>
    private static async Task StopAgainAsync(IHost host)
    {
        await host.WaitForShutdownAsync();
    }

    /// <summary>
    /// <c>app.RunAsync()</c> disposes the host once its own stop ends, while another stop (a test host's) may still be
    /// stopping the queue: the disposal finds nothing left to stop.
    /// </summary>
    private static async Task DisposeWhileStoppingAsync()
    {
        var queue = $"disposed-while-stopping-{Guid.NewGuid():N}";
        using var strategy = new HeldBusStrategy(address => address == queue);
        using var logs = new FakeLoggerProvider();
        using var host = await StartAsync(queue, strategy, logs);
        var queueHost = QueueHost(host, queue);
        await InMemoryServices.WaitUntilAsync(() => queueHost.IsConsuming);
        strategy.HoldNextDisposal();

        var stopping = Task.Run(() => queueHost.StopAsync(CancellationToken.None));
        await strategy.DisposalHeld.WaitAsync(GiveUpAfter);
        queueHost.Dispose();
        Lines(logs, StoppedLine).ShouldBe(0);

        strategy.ReleaseDisposal();
        await stopping.WaitAsync(GiveUpAfter);
        Lines(logs, StoppedLine).ShouldBe(1);
        strategy.Disposed.ShouldBe(1);
        queueHost.IsConsuming.ShouldBeFalse();
    }

    /// <summary>
    /// The health gate stops the queue's bus (unhealthy) and starts a new one (healthy again); the host stops while
    /// that start is under way, without waiting for it: disposed (<c>app.RunAsync()</c>'s end), or stopped past its
    /// shutdown timeout. The bus that was starting must not stay running, nor count as consuming.
    /// </summary>
    private static async Task StopWhileRestartingAsync(HostStop stop)
    {
        var queue = $"restarting-{Guid.NewGuid():N}";
        var upstream = new Upstream();
        using var strategy = new HeldBusStrategy(address => address == queue);
        using var logs = new FakeLoggerProvider();
        using var host = await StartAsync(queue, strategy, logs, upstream);
        var queueHost = QueueHost(host, queue);
        await InMemoryServices.WaitUntilAsync(() => queueHost.IsConsuming);

        upstream.IsHealthy = false;
        await InMemoryServices.WaitUntilAsync(() => !queueHost.IsConsuming && strategy.Disposed == 1);
        strategy.HoldNextStart();
        upstream.IsHealthy = true;
        await strategy.StartHeld.WaitAsync(GiveUpAfter);

        await StopAsync(queueHost, stop);
        strategy.ReleaseStart();
        await queueHost.ExecuteTask!.WaitAsync(GiveUpAfter);

        queueHost.IsConsuming.ShouldBeFalse();
        strategy.Built.ShouldBe(2);
        strategy.Disposed.ShouldBe(2);   // the bus that was starting too
        Lines(logs, StartedLine).ShouldBe(1);   // the first start only: nothing consumed after the host stopped
        Lines(logs, StoppedLine).ShouldBe(1);   // the health gate's
    }

    private static async Task StopAsync(
        RateLimitedQueueHost<StopRequest, StopResponse> queueHost,
        HostStop stop)
    {
        if (stop == HostStop.Dispose)
        {
            queueHost.Dispose();
            return;
        }

        // The host's shutdown timeout has passed: StopAsync stops waiting for the queue's loop.
        using var shutdownTimeout = new CancellationTokenSource();
        await shutdownTimeout.CancelAsync();
        await queueHost.StopAsync(shutdownTimeout.Token).WaitAsync(GiveUpAfter);
    }

    private static Task<IHost> StartAsync(
        string queue,
        HeldBusStrategy strategy,
        FakeLoggerProvider logs,
        Upstream? upstream = null)
        => InMemoryServices.StartAsync(
            new InMemNetwork(),
            bus => bus
                .UseConfigurationStrategy(strategy)
                .AddRateLimitedQueue<StopRequest, StopResponse, StopResponder>(queue, options =>
                {
                    if (upstream is null)
                        return;
                    options.HealthCheckTag = HealthTag;
                    options.HealthCheckInterval = TimeSpan.FromMilliseconds(50);
                }),
            services =>
            {
                services.AddLogging(logging => logging.AddProvider(logs));
                if (upstream is not null)
                    services.AddHealthChecks().AddCheck(HealthTag, upstream.Check, tags: [HealthTag]);
            });

    private static IQueueConsumer Consumer(
        IHost host,
        string queue)
        => host.Services.GetServices<IQueueConsumer>().Single(consumer => consumer.QueueName == queue);

    private static RateLimitedQueueHost<StopRequest, StopResponse> QueueHost(
        IHost host,
        string queue)
        => host.Services.GetRequiredKeyedService<RateLimitedQueueHost<StopRequest, StopResponse>>(queue);

    private static int Lines(
        FakeLoggerProvider logs,
        string startingWith)
        => logs.Collector.GetSnapshot().Count(record =>
            record.Category is { } category
            && category.Contains("RateLimitedQueueHost", StringComparison.Ordinal)
            && record.Message.StartsWith(startingWith, StringComparison.Ordinal));

    private enum HostStop
    {
        Dispose,
        StopAsyncPastItsShutdownTimeout,
    }

    internal sealed class Upstream
    {
        public volatile bool IsHealthy = true;

        public HealthCheckResult Check()
            => IsHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }

    internal sealed record StopRequest(string Key);

    internal sealed record StopResponse(string Key);

    internal sealed class StopResponder : IRequestResponder<StopRequest, StopResponse>
    {
        public Task<Result<StopResponse>> HandleAsync(StopRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(new StopResponse(request.Key)));
    }
}
