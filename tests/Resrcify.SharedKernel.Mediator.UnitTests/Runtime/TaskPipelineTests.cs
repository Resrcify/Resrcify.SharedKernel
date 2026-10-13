using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Runtime;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Runtime;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class TaskPipelineTests
{
    [Fact]
    public async Task Invoke_WithBehaviorsAndRequestBehaviors_RunsThemOutermostFirstThenTheHandler()
    {
        var log = new List<string>();
        var pipeline = new TaskPipeline<Echo, string>(
            new EchoHandler(log),
            [new LoggingBehavior("outer", log), new LoggingBehavior("inner", log)],
            [new LoggingRequestBehavior("request-outer", log), new LoggingRequestBehavior("request-inner", log)]);

        var response = await pipeline.Invoke(new Echo("hi"), CancellationToken.None);

        response.ShouldBe("hi");
        log.ShouldBe(
        [
            "outer:before", "inner:before", "request-outer:before", "request-inner:before",
            "handler:hi",
            "request-inner:after", "request-outer:after", "inner:after", "outer:after",
        ]);
    }

    [Fact]
    public async Task Invoke_WhenARequestBehaviorReplacesTheRequest_HandsTheNewRequestOn()
    {
        var log = new List<string>();
        var pipeline = new TaskPipeline<Echo, string>(
            new EchoHandler(log),
            [],
            [new ReplacingRequestBehavior("replaced")]);

        var response = await pipeline.Invoke(new Echo("original"), CancellationToken.None);

        response.ShouldBe("replaced");
        log.ShouldBe(["handler:replaced"]);
    }

    [Fact]
    public async Task Invoke_WhenABehaviorCallsNextTwice_RunsTheRestOfThePipelineTwice()
    {
        var log = new List<string>();
        var pipeline = new TaskPipeline<Echo, string>(
            new EchoHandler(log),
            [new RetryingBehavior(), new LoggingBehavior("inner", log)],
            []);

        var response = await pipeline.Invoke(new Echo("hi"), CancellationToken.None);

        response.ShouldBe("hi");
        log.ShouldBe(["inner:before", "handler:hi", "inner:after", "inner:before", "handler:hi", "inner:after"]);
    }

    [Fact]
    public async Task Invoke_WithoutBehaviors_CallsTheHandler()
    {
        var log = new List<string>();
        var pipeline = new TaskPipeline<Echo, string>(new EchoHandler(log), [], []);

        (await pipeline.Invoke(new Echo("hi"), CancellationToken.None)).ShouldBe("hi");
        log.ShouldBe(["handler:hi"]);
    }

    [Fact]
    public async Task Invoke_ConcurrentlyWithDifferentRequests_KeepsEachCallsRequest()
    {
        var pipeline = new TaskPipeline<Echo, string>(
            new EchoHandler([]),
            [new YieldingBehavior(), new YieldingBehavior()],
            []);

        var responses = await Task.WhenAll(
            pipeline.Invoke(new Echo("a"), CancellationToken.None),
            pipeline.Invoke(new Echo("b"), CancellationToken.None),
            pipeline.Invoke(new Echo("c"), CancellationToken.None));

        responses.ShouldBe(["a", "b", "c"]);
    }

    internal sealed record Echo(string Text) : IRequest<string>;

    private sealed class EchoHandler(List<string> log) : IRequestHandler<Echo, string>
    {
        public Task<string> Handle(Echo request, CancellationToken cancellationToken)
        {
            lock (log)
                log.Add($"handler:{request.Text}");
            return Task.FromResult(request.Text);
        }
    }

    private sealed class LoggingBehavior(string name, List<string> log) : IPipelineBehavior<Echo, string>
    {
        public async Task<string> Handle(Echo request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            log.Add($"{name}:before");
            var response = await next(cancellationToken);
            log.Add($"{name}:after");
            return response;
        }
    }

    private sealed class LoggingRequestBehavior(string name, List<string> log) : IRequestPipelineBehavior<Echo, string>
    {
        public async Task<string> Handle(Echo request, RequestExecutionDelegate<Echo, string> next, CancellationToken cancellationToken)
        {
            log.Add($"{name}:before");
            var response = await next(request, cancellationToken);
            log.Add($"{name}:after");
            return response;
        }
    }

    private sealed class ReplacingRequestBehavior(string text) : IRequestPipelineBehavior<Echo, string>
    {
        public Task<string> Handle(Echo request, RequestExecutionDelegate<Echo, string> next, CancellationToken cancellationToken)
            => next(request with { Text = text }, cancellationToken);
    }

    private sealed class RetryingBehavior : IPipelineBehavior<Echo, string>
    {
        public async Task<string> Handle(Echo request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _ = await next(cancellationToken);
            return await next(cancellationToken);
        }
    }

    private sealed class YieldingBehavior : IPipelineBehavior<Echo, string>
    {
        public async Task<string> Handle(Echo request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return await next(cancellationToken);
        }
    }
}
