using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

/// <summary>Several queues gated on one health-check tag share one check.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class HealthGateTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Queues_ShouldShareOneHealthCheck_WhenTheyAreGatedOnTheSameTag()
    {
        var upstream = new Upstream();
        using var host = await StartAsync(upstream, queues: 4);

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // One check every 200 ms is about 8 in 1.5 s; four queues checking on their own would be about 30.
        upstream.Checks.ShouldBeInRange(4, 12);
    }

    [Fact]
    public async Task Queues_ShouldAllStepAsideAndComeBack_WhenTheSharedCheckChanges()
    {
        var upstream = new Upstream();
        using var host = await StartAsync(upstream, queues: 2);
        var queues = host.Services.GetServices<IQueueConsumer>().ToList();
        await InMemoryServices.WaitUntilAsync(() => queues.All(queue => queue.IsConsuming));

        upstream.IsHealthy = false;
        await InMemoryServices.WaitUntilAsync(() => queues.All(queue => !queue.IsConsuming));

        upstream.IsHealthy = true;
        await InMemoryServices.WaitUntilAsync(() => queues.All(queue => queue.IsConsuming));
    }

    private static Task<IHost> StartAsync(Upstream upstream, int queues)
        => InMemoryServices.StartAsync(
            new InMemNetwork(),
            bus =>
            {
                static void Gate(RateLimitedQueueOptions options)
                {
                    options.HealthCheckTag = "upstream";
                    options.HealthCheckInterval = Interval;
                }
                bus.AddRateLimitedQueue<Ask1, Answer, Answerer<Ask1>>(Gate);
                if (queues > 1)
                    bus.AddRateLimitedQueue<Ask2, Answer, Answerer<Ask2>>(Gate);
                if (queues > 2)
                    bus.AddRateLimitedQueue<Ask3, Answer, Answerer<Ask3>>(Gate);
                if (queues > 3)
                    bus.AddRateLimitedQueue<Ask4, Answer, Answerer<Ask4>>(Gate);
            },
            services => services
                .AddHealthChecks()
                .AddCheck("upstream", upstream.Check, tags: ["upstream"]));

    internal sealed class Upstream
    {
        private int _checks;

        public volatile bool IsHealthy = true;

        public int Checks => Volatile.Read(ref _checks);

        public HealthCheckResult Check()
        {
            Interlocked.Increment(ref _checks);
            return IsHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
    }

    internal sealed record Ask1(string Value);

    internal sealed record Ask2(string Value);

    internal sealed record Ask3(string Value);

    internal sealed record Ask4(string Value);

    internal sealed record Answer(string Value);

    internal sealed class Answerer<TAsk> : IRequestResponder<TAsk, Answer>
        where TAsk : class
    {
        public Task<Result<Answer>> HandleAsync(TAsk request, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<Answer>>(new Answer("ok"));
    }
}
