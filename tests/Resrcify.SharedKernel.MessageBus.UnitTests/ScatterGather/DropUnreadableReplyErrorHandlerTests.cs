using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DropUnreadableReplyErrorHandlerTests
{
    [Fact]
    public async Task HandlePoisonMessage_ShouldDropAndCountTheReply_WhenTheRequesterCantReadIt()
    {
        using var metrics = new MetricsCapture();
        var network = new InMemNetwork();
        var queue = $"lookups-{Guid.NewGuid():N}";
        // The responder answers with a class the requester has never heard of.
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<StrangeLookup, StrangeAnswer, StrangeResponder>(queue));
        using var requester = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRequest<StrangeLookup, ExpectedAnswer>(queue).AddScatterGather());

        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<StrangeLookup, ExpectedAnswer>(new StrangeLookup("han"), TimeSpan.FromSeconds(1));
        await InMemoryServices.WaitUntilAsync(() => metrics.Count("messagebus.scatter_gather.replies outcome=unreadable") >= 1);

        reply.Errors.ShouldHaveSingleItem().Type.ShouldBe(ErrorType.Timeout);
        network.Count("error").ShouldBe(0);
    }

    internal sealed record StrangeLookup(string Name);

    internal sealed record StrangeAnswer(string Text);

    internal sealed record ExpectedAnswer(string Text);

    internal sealed class StrangeResponder : IRequestResponder<StrangeLookup, StrangeAnswer>
    {
        public Task<Result<StrangeAnswer>> HandleAsync(StrangeLookup request, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<StrangeAnswer>>(new StrangeAnswer("?"));
    }
}
