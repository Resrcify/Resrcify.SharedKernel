using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

/// <summary>A queue answered through the mediator, without a responder class of its own.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MediatorRequestResponderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task HandleAsync_ShouldAnswerWithTheMediatorsResult_WhenItSucceeds()
    {
        var (reply, _) = await AskAsync("han");

        reply.Value.ShouldBe(new PlayerAnswer("found han"));
    }

    [Fact]
    public async Task HandleAsync_ShouldAnswerWithTheErrors_WhenTheRequestIsAtFault()
    {
        using var metrics = new MetricsCapture();
        var (reply, calls) = await AskAsync("unknown");

        reply.Errors.ShouldHaveSingleItem().Code.ShouldBe("Player.NotFound");
        calls.Count.ShouldBe(1);
        CountOf(metrics, "messagebus.requests.handled", "outcome=failure").ShouldBe(1);
        CountOf(metrics, "messagebus.scatter_gather.items", "outcome=failed").ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_ShouldTryAgainThenAnswerTheFailure_WhenTheFailureMayPass()
    {
        // Sent back to the queue 4 times (0.5, 1, 2, 4 s apart), then answered: the requester gets the failure itself.
        using var metrics = new MetricsCapture();
        var (reply, calls) = await AskAsync("upstream-down", timeout: TimeSpan.FromSeconds(20));

        reply.Errors.ShouldHaveSingleItem().Code.ShouldBe("Upstream.Down");
        calls.Count.ShouldBe(5);
        CountOf(metrics, "messagebus.requests.handled", "outcome=retried").ShouldBe(4);
        CountOf(metrics, "messagebus.requests.handled", "outcome=gave_up").ShouldBe(1);
        CountOf(metrics, "messagebus.scatter_gather.items", "outcome=gave_up").ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_ShouldTryAgainThenAnswerAFailure_WhenTheHandlerThrows()
    {
        var (reply, calls) = await AskAsync("throws", timeout: TimeSpan.FromSeconds(20));

        reply.Errors.ShouldHaveSingleItem().Code.ShouldBe("PlayerQuestion.ResponderFailed");
        calls.Count.ShouldBe(5);
    }

    [Fact]
    public async Task HandleAsync_ShouldLeaveTheRequestUnanswered_WhenItsRequesterStopsWaitingDuringTheRetries()
    {
        var (reply, calls) = await AskAsync("upstream-down");   // 3 s: the tries outlast it

        reply.Errors.ShouldHaveSingleItem().Type.ShouldBe(ErrorType.Timeout);
        calls.Count.ShouldBeLessThan(5);
    }

    [Fact]
    public async Task HandleAsync_ShouldAnswerTheFailure_WhenTheQueuesPolicySaysSo()
    {
        var (reply, calls) = await AskAsync("upstream-down", answerEveryFailure: true);

        reply.Errors.ShouldHaveSingleItem().Code.ShouldBe("Upstream.Down");
        calls.Count.ShouldBe(1);
    }

    /// <summary>This class' measurements of one counter and outcome (its tests run one at a time; others use other queues).</summary>
    private static int CountOf(MetricsCapture metrics, string counter, string outcome)
        => metrics.Measurements.Count(measurement =>
            measurement.StartsWith(counter + " ", StringComparison.Ordinal)
            && measurement.Contains(outcome, StringComparison.Ordinal)
            && measurement.Contains(nameof(PlayerQuestion), StringComparison.Ordinal));

    private static async Task<(Result<PlayerAnswer> Reply, LookupCalls Calls)> AskAsync(
        string name,
        bool answerEveryFailure = false,
        TimeSpan? timeout = null)
    {
        var network = new InMemNetwork();
        var calls = new LookupCalls();
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<PlayerQuestion, PlayerAnswer>(
                question => new LookUpPlayer(question.Name),
                options =>
                {
                    options.PerSecond = 100;
                    if (answerEveryFailure)
                        options.AnswerFailure = _ => true;
                }),
            services => services
                .AddSingleton(calls)
                .AddMediator(typeof(MediatorRequestResponderTests).Assembly));
        using var requester = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRequest<PlayerQuestion, PlayerAnswer>().AddScatterGather());

        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<PlayerQuestion, PlayerAnswer>(new PlayerQuestion(name), timeout ?? Timeout);
        return (reply, calls);
    }

    internal sealed record PlayerQuestion(string Name);

    internal sealed record PlayerAnswer(string Text);

    internal sealed record LookUpPlayer(string Name) : IRequest<Result<PlayerAnswer>>;

    internal sealed class LookupCalls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add()
            => Interlocked.Increment(ref _count);
    }

    internal sealed class LookUpPlayerHandler(LookupCalls calls) : IRequestHandler<LookUpPlayer, Result<PlayerAnswer>>
    {
        public Task<Result<PlayerAnswer>> Handle(LookUpPlayer request, CancellationToken cancellationToken)
        {
            calls.Add();
            if (request.Name == "throws")
                throw new InvalidOperationException("A bug in the handler.");
            Result<PlayerAnswer> result = request.Name switch
            {
                "unknown" => new Error("Player.NotFound", "No such player", ErrorType.NotFound),
                "upstream-down" => new Error("Upstream.Down", "The game is down", ErrorType.ExternalFailure),
                _ => new PlayerAnswer("found " + request.Name),
            };
            return Task.FromResult(result);
        }
    }
}
