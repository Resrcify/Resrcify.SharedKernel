using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
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
public sealed class StreamPipelineTests
{
    [Fact]
    public async Task Invoke_WithBehaviors_RunsThemOutermostFirstAroundTheHandlersItems()
    {
        var pipeline = new StreamPipeline<Count, string>(
            new CountHandler(),
            [new TaggingBehavior("outer"), new TaggingBehavior("inner")]);

        var items = new List<string>();
        await foreach (var item in pipeline.Invoke(new Count(2), CancellationToken.None))
            items.Add(item);

        items.ShouldBe(["outer(inner(0))", "outer(inner(1))"]);
    }

    [Fact]
    public async Task Invoke_WithoutBehaviors_StreamsTheHandlersItems()
    {
        var pipeline = new StreamPipeline<Count, string>(new CountHandler(), []);

        var items = new List<string>();
        await foreach (var item in pipeline.Invoke(new Count(3), CancellationToken.None))
            items.Add(item);

        items.ShouldBe(["0", "1", "2"]);
    }

    internal sealed record Count(int To) : IStreamRequest<string>;

    private sealed class CountHandler : IStreamRequestHandler<Count, string>
    {
        public async IAsyncEnumerable<string> Handle(Count request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var number in Enumerable.Range(0, request.To))
            {
                await Task.Yield();
                yield return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    private sealed class TaggingBehavior(string tag) : IStreamPipelineBehavior<Count, string>
    {
        public async IAsyncEnumerable<string> Handle(
            Count request,
            StreamHandlerDelegate<string> next,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in next(cancellationToken))
                yield return $"{tag}({item})";
        }
    }
}
