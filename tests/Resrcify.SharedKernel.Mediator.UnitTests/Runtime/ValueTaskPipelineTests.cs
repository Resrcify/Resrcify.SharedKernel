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
public sealed class ValueTaskPipelineTests
{
    [Fact]
    public async Task Invoke_WithBehaviorsAndRequestBehaviors_RunsThemOutermostFirstThenTheHandler()
    {
        var log = new List<string>();
        var pipeline = new ValueTaskPipeline<Echo, string>(
            new EchoHandler(log),
            [new LoggingBehavior("outer", log), new LoggingBehavior("inner", log)],
            [new LoggingRequestBehavior("request", log)]);

        var response = await pipeline.Invoke(new Echo("hi"), CancellationToken.None);

        response.ShouldBe("hi");
        log.ShouldBe(["outer:before", "inner:before", "request:before", "handler:hi", "request:after", "inner:after", "outer:after"]);
    }

    [Fact]
    public async Task Invoke_WhenABehaviorCallsNextTwice_RunsTheRestOfThePipelineTwice()
    {
        var log = new List<string>();
        var pipeline = new ValueTaskPipeline<Echo, string>(new EchoHandler(log), [new RetryingBehavior()], []);

        (await pipeline.Invoke(new Echo("hi"), CancellationToken.None)).ShouldBe("hi");
        log.ShouldBe(["handler:hi", "handler:hi"]);
    }

    internal sealed record Echo(string Text) : IRequest<string>;

    private sealed class EchoHandler(List<string> log) : IValueTaskRequestHandler<Echo, string>
    {
        public ValueTask<string> Handle(Echo request, CancellationToken cancellationToken)
        {
            log.Add($"handler:{request.Text}");
            return ValueTask.FromResult(request.Text);
        }
    }

    private sealed class LoggingBehavior(string name, List<string> log) : IValueTaskPipelineBehavior<Echo, string>
    {
        public async ValueTask<string> Handle(Echo request, ValueTaskRequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            log.Add($"{name}:before");
            var response = await next(cancellationToken);
            log.Add($"{name}:after");
            return response;
        }
    }

    private sealed class LoggingRequestBehavior(string name, List<string> log) : IValueTaskRequestPipelineBehavior<Echo, string>
    {
        public async ValueTask<string> Handle(Echo request, ValueTaskRequestExecutionDelegate<Echo, string> next, CancellationToken cancellationToken)
        {
            log.Add($"{name}:before");
            var response = await next(request, cancellationToken);
            log.Add($"{name}:after");
            return response;
        }
    }

    private sealed class RetryingBehavior : IValueTaskPipelineBehavior<Echo, string>
    {
        public async ValueTask<string> Handle(Echo request, ValueTaskRequestHandlerDelegate<string> next, CancellationToken cancellationToken)
        {
            _ = await next(cancellationToken);
            return await next(cancellationToken);
        }
    }
}
