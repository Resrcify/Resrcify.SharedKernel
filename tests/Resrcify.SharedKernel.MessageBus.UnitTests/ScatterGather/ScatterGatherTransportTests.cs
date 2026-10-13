using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

/// <summary>The scatter-gather client on the in-memory transport: timeouts, empty batches, streams.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ScatterGatherTransportTests
{
    [Fact]
    public async Task GatherAsync_ShouldAnswerAtOnce_WhenThereAreNoRequests()
    {
        using var requester = await InMemoryServices.StartAsync(new InMemNetwork(), bus => bus.AddScatterGather());
        var started = TimeProvider.System.GetTimestamp();

        var gathered = await Client(requester).GatherAsync<Echo, Echoed>(new Dictionary<string, Echo>(), TimeSpan.FromSeconds(30));
        var replies = await Client(requester).StreamAsync<Echo, Echoed>(new Dictionary<string, Echo>(), TimeSpan.FromSeconds(30)).ToListAsync();

        gathered.IsComplete.ShouldBeTrue();
        replies.ShouldBeEmpty();
        TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(60 * 24 * 60 * 60 * 1000.0)]
    public async Task GatherAsync_ShouldThrowBeforeSendingAnything_WhenTheTimeoutCantBeMet(double milliseconds)
    {
        var network = new InMemNetwork();
        var queue = $"echo-{Guid.NewGuid():N}";
        var calls = new EchoCalls();
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<Echo, Echoed, EchoHandler>(queue),
            services => services.AddSingleton(calls));
        using var requester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<Echo, Echoed>(queue).AddScatterGather());
        var timeout = TimeSpan.FromMilliseconds(milliseconds);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Client(requester).RequestAsync<Echo, Echoed>(new Echo("a"), timeout));
        Should.Throw<ArgumentOutOfRangeException>(() => Client(requester).StreamAsync<Echo, Echoed>(Requests(1), timeout));

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        calls.Count.ShouldBe(0);
    }

    [Fact]
    public async Task StreamAsync_ShouldYieldEveryReplyAcceptedBeforeTheTimeout_WhenTheReaderIsSlow()
    {
        var network = new InMemNetwork();
        var queue = $"echo-{Guid.NewGuid():N}";
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<Echo, Echoed, EchoHandler>(queue),
            services => services.AddSingleton(new EchoCalls()));
        using var requester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<Echo, Echoed>(queue).AddScatterGather());

        var read = new List<string>();
        await foreach (var reply in Client(requester).StreamAsync<Echo, Echoed>(Requests(3), TimeSpan.FromSeconds(1)))
        {
            read.Add(reply.Key);
            // All three are in by the first one; reading them takes longer than the timeout.
            await Task.Delay(TimeSpan.FromMilliseconds(600));
        }

        read.ShouldBe(["0", "1", "2"], ignoreOrder: true);
    }

    [Fact]
    public async Task StreamAsync_ShouldThrow_WhenTheStreamIsEnumeratedAgain()
    {
        var network = new InMemNetwork();
        var queue = $"echo-{Guid.NewGuid():N}";
        var calls = new EchoCalls();
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<Echo, Echoed, EchoHandler>(queue),
            services => services.AddSingleton(calls));
        using var requester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<Echo, Echoed>(queue).AddScatterGather());
        var stream = Client(requester).StreamAsync<Echo, Echoed>(Requests(3), TimeSpan.FromSeconds(5));

        (await stream.ToListAsync()).Count.ShouldBe(3);
        await Should.ThrowAsync<InvalidOperationException>(async () => await stream.ToListAsync());

        calls.Count.ShouldBe(3);   // sent once
    }

    [Fact]
    public async Task OnBrokerRecovered_ShouldRestartTheReplyBusAgain_UntilARestartSucceeds()
    {
        // The broker is back, but the reply queue can't be declared yet (its node is still down): the restart used to
        // fail unobserved, leaving no reply bus, and every gather failing, until the next outage or a pod restart.
        var network = new InMemNetwork();
        var queue = $"echo-{Guid.NewGuid():N}";
        var strategy = new FailingStartStrategy(address => address?.Contains(".replies.", StringComparison.Ordinal) == true);
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<Echo, Echoed, EchoHandler>(queue),
            services => services.AddSingleton(new EchoCalls()));
        using var requester = await InMemoryServices.StartAsync(
            network,
            bus => bus.UseConfigurationStrategy(strategy).AddRequest<Echo, Echoed>(queue).AddScatterGather());
        var transport = requester.Services.GetRequiredService<ScatterGatherTransport>();
        transport.IsRunning.ShouldBeTrue();
        strategy.Arm(2);

        transport.OnBrokerRecovered();

        // Two restarts fail (1 s apart); the bus is down meanwhile, and the next one, 2 s later, brings it back.
        await InMemoryServices.WaitUntilAsync(() => strategy.Failed == 2, TimeSpan.FromSeconds(10));
        transport.IsRunning.ShouldBeFalse();
        transport.IsStarted.ShouldBeTrue();
        await InMemoryServices.WaitUntilAsync(() => transport.IsRunning, TimeSpan.FromSeconds(10));

        var reply = await Client(requester).RequestAsync<Echo, Echoed>(new Echo("back"), TimeSpan.FromSeconds(5));
        reply.Value.Text.ShouldBe("back");
    }

    [Fact]
    public async Task RequestAsync_WhileTheReplyBusRestarts_WaitsForItInsteadOfThrowing()
    {
        // A node of the cluster went down: the reply bus restarts, and a request sent meanwhile used to throw "The
        // scatter-gather transport has not started" out of RequestAsync.
        var (responder, requester, transport, strategy) = await StartRestartingRequesterAsync();
        using var respond = responder;
        using var request = requester;
        strategy.Arm(2);

        transport.OnBrokerRecovered();
        await InMemoryServices.WaitUntilAsync(() => strategy.Failed == 1, TimeSpan.FromSeconds(10));
        transport.IsRunning.ShouldBeFalse();

        // The bus is back within about 3 s (two failed restarts, 1 s and 2 s apart): within the request's timeout.
        var reply = await Client(requester).RequestAsync<Echo, Echoed>(new Echo("waited"), TimeSpan.FromSeconds(15));

        reply.IsSuccess.ShouldBeTrue();
        reply.Value.Text.ShouldBe("waited");
    }

    [Fact]
    public async Task RequestAsync_WhenTheReplyBusIsNotBackBeforeTheTimeout_IsUnanswered()
    {
        var (responder, requester, transport, strategy) = await StartRestartingRequesterAsync();
        using var respond = responder;
        using var request = requester;
        strategy.Arm(int.MaxValue);

        transport.OnBrokerRecovered();
        await InMemoryServices.WaitUntilAsync(() => strategy.Failed == 1, TimeSpan.FromSeconds(10));
        var started = DateTime.UtcNow;

        var reply = await Client(requester).RequestAsync<Echo, Echoed>(new Echo("lost"), TimeSpan.FromSeconds(1));

        reply.IsFailure.ShouldBeTrue();
        reply.Errors[0].Type.ShouldBe(ErrorType.Timeout);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(3));   // within its timeout, not after it
    }

    private static async Task<(
        Microsoft.Extensions.Hosting.IHost Responder,
        Microsoft.Extensions.Hosting.IHost Requester,
        ScatterGatherTransport Transport,
        FailingStartStrategy Strategy)> StartRestartingRequesterAsync()
    {
        var network = new InMemNetwork();
        var queue = $"echo-{Guid.NewGuid():N}";
        var strategy = new FailingStartStrategy(address => address?.Contains(".replies.", StringComparison.Ordinal) == true);
        var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<Echo, Echoed, EchoHandler>(queue),
            services => services.AddSingleton(new EchoCalls()));
        var requester = await InMemoryServices.StartAsync(
            network,
            bus => bus.UseConfigurationStrategy(strategy).AddRequest<Echo, Echoed>(queue).AddScatterGather());
        var transport = requester.Services.GetRequiredService<ScatterGatherTransport>();
        transport.IsRunning.ShouldBeTrue();
        return (responder, requester, transport, strategy);
    }

    private static IScatterGatherClient Client(Microsoft.Extensions.Hosting.IHost host)
        => host.Services.GetRequiredService<IScatterGatherClient>();

    private static Dictionary<string, Echo> Requests(int count)
        => Enumerable.Range(0, count).ToDictionary(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture), index => new Echo($"{index}"));

    internal sealed record Echo(string Text);

    internal sealed record Echoed(string Text);

    internal sealed class EchoCalls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add()
            => Interlocked.Increment(ref _count);
    }

    internal sealed class EchoHandler(EchoCalls calls) : IRequestResponder<Echo, Echoed>
    {
        public Task<Result<Echoed>> HandleAsync(Echo request, CancellationToken cancellationToken = default)
        {
            calls.Add();
            return Task.FromResult(Result.Success(new Echoed(request.Text)));
        }
    }
}
