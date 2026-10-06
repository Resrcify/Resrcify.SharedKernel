using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Diagnostics;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests;

/// <summary>
/// Scatter-gather end to end over real RabbitMQ and Postgres: an aggregate raises an event, the outbox's
/// scatter-gather lane runs its <c>IScatterGatherHandler</c> (send, wait in memory, gather), and the
/// gathered result commits with the outbox. Responders are rate-limited queues.
/// </summary>
[Collection(BusCollection.Name)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ScatterGatherTests(BusFixture bus, ITestOutputHelper output)
{
    private readonly string _requestQueue = $"ping-{Guid.NewGuid():N}";

    [Fact]
    public async Task ScatterGather_WhenEveryItemAnswers_GathersAndCommitsWithTheOutbox()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10);

        var started = DateTime.UtcNow;
        var runId = await requester.StartRunAsync(items: 20);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        var gatheredAfter = run.GatheredAtUtc!.Value - started;
        output.WriteLine($"gathered after {gatheredAfter.TotalSeconds:F1}s");
        run.Results.ShouldBe(20);
        run.Unanswered.ShouldBe(0);
        gatheredAfter.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        (await requester.PendingScatterMessagesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ScatterGather_WhenResponderHasNoResult_CountsTheItemAsFailed()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(
            perSecond: 100,
            burst: 10,
            respond: request => ItemNumber(request) % 2 == 0
                ? new PingResponse("pong")
                : NoData(request));

        var runId = await requester.StartRunAsync(items: 20);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        run.Results.ShouldBe(10);
        run.Failures.ShouldBe(10);
        run.Unanswered.ShouldBe(0);
        RequesterHost.PingScatterGatherHandler.LastFailures["item-1"].ShouldBe([NoData(new PingRequest("ping 1"))]);
    }

    [Fact]
    public async Task ScatterGather_WhenItemsNeverAnswer_GathersThemAsUnansweredAtTheTimeout()
    {
        UseTimeout(TimeSpan.FromSeconds(4));
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(
            perSecond: 100,
            burst: 10,
            respond: request => ItemNumber(request) % 2 == 0
                ? new PingResponse("pong")
                : throw new InvalidOperationException("upstream keeps failing"));

        var started = DateTime.UtcNow;
        var runId = await requester.StartRunAsync(items: 20);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        run.Results.ShouldBe(10);
        run.Unanswered.ShouldBe(10);
        (run.GatheredAtUtc!.Value - started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task ScatterGather_WhenUnitOfWorkDoesNotCommit_SendsNothing()
    {
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10);

        var runId = await requester.StartRunAsync(items: 10, commit: false);
        await Task.Delay(TimeSpan.FromSeconds(3));

        (await requester.FindRunAsync(runId)).ShouldBeNull();
        pod.Handled.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScatterGather_WhileWaitingForReplies_DoesNotHoldUpTheRegularOutbox()
    {
        UseTimeout(TimeSpan.FromSeconds(15));
        await using var requester = await StartRequesterAsync();   // no responder: the batch waits its full timeout

        var runId = await requester.StartRunAsync(items: 5);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var noteId = await requester.CreateNoteAsync();
        var noteCreated = DateTime.UtcNow;

        var giveUpAt = DateTime.UtcNow.AddSeconds(10);
        while ((await requester.FindNoteAsync(noteId))?.HandledAtUtc is null && DateTime.UtcNow < giveUpAt)
            await Task.Delay(100);

        var note = await requester.FindNoteAsync(noteId);
        output.WriteLine($"note handled after {(note!.HandledAtUtc!.Value - noteCreated).TotalSeconds:F1}s");
        note.HandledAtUtc.ShouldNotBeNull();
        (await requester.FindRunAsync(runId))!.GatheredAtUtc.ShouldBeNull();   // still waiting
    }

    [Fact]
    public async Task ScatterGather_WithSameDedupKeyInFlight_DropsTheSecondEvent()
    {
        UseTimeout(TimeSpan.FromSeconds(10));
        await using var requester = await StartRequesterAsync();   // no responder: the first run stays in flight

        await requester.StartRunAsync(items: 3, dedupGroup: "shard-1");
        await Task.Delay(TimeSpan.FromSeconds(1));
        await requester.StartRunAsync(items: 3, dedupGroup: "shard-1");
        await requester.StartRunAsync(items: 3, dedupGroup: "shard-2");

        (await requester.PendingScatterMessagesAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task RateLimitedQueue_WithTwoInstances_HoldsEachLimitAndDoublesCapacity()
    {
        const int perSecond = 10;
        const int burst = 2;
        UseTimeout(TimeSpan.FromSeconds(30));
        await using var requester = await StartRequesterAsync();
        await using var podA = await StartPodAsync(perSecond, burst);
        await using var podB = await StartPodAsync(perSecond, burst);

        var runId = await requester.StartRunAsync(items: 80);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(45));

        var handled = podA.Handled.Concat(podB.Handled).ToList();
        var duration = handled.Max() - handled.Min();
        output.WriteLine($"pod-a {podA.Handled.Count}, pod-b {podB.Handled.Count}, drained in {duration.TotalSeconds:F1}s");
        output.WriteLine($"busiest second: pod-a {BusiestSecond(podA.Handled)}, pod-b {BusiestSecond(podB.Handled)}");
        run.Results.ShouldBe(80);
        BusiestSecond(podA.Handled).ShouldBeLessThanOrEqualTo(perSecond + burst + 1);
        BusiestSecond(podB.Handled).ShouldBeLessThanOrEqualTo(perSecond + burst + 1);
        // One instance alone needs ~8 s for 80 items at 10/s; two need ~4 s.
        duration.TotalSeconds.ShouldBeInRange(3.0, 6.5);
    }

    [Fact]
    public async Task RateLimitedQueue_WhenAnInstanceTurnsUnhealthy_HandsItsMessagesToTheOthers()
    {
        UseTimeout(TimeSpan.FromSeconds(45));
        await using var requester = await StartRequesterAsync();
        await using var podA = await StartPodAsync(perSecond: 4, prefetch: 10);
        await using var podB = await StartPodAsync(perSecond: 4, prefetch: 10);

        var runId = await requester.StartRunAsync(items: 40);
        await WaitUntilAsync(() => podA.Handled.Count >= 3);
        podA.MarkUnhealthy();
        var markedAt = DateTime.UtcNow;

        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(60));

        output.WriteLine($"pod-a {podA.Handled.Count} (after unhealthy: {podA.Handled.Count(x => x > markedAt.AddSeconds(3))}), pod-b {podB.Handled.Count}");
        run.Results.ShouldBe(40);
        podA.Handled.Count(x => x > markedAt.AddSeconds(3)).ShouldBe(0);
    }

    [Fact]
    public async Task RateLimitedQueue_WhenAnInstanceStopsMidRequest_CancelsItAndAnotherInstanceAnswers()
    {
        UseTimeout(TimeSpan.FromSeconds(60));
        await using var requester = await StartRequesterAsync();
        await using var slow = await StartPodAsync(perSecond: 100, burst: 10, delay: TimeSpan.FromSeconds(45));

        var runId = await requester.StartRunAsync(items: 3);
        await WaitUntilAsync(() => slow.Handled.Count >= 3);
        await using var fast = await StartPodAsync(perSecond: 100, burst: 10);
        var stoppedAt = DateTime.UtcNow;
        slow.MarkUnhealthy();

        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        var finishedAfter = run.GatheredAtUtc!.Value - stoppedAt;
        output.WriteLine($"cancelled on the stopped instance: {slow.Cancelled}, answered by the other: {fast.Handled.Count}, after {finishedAfter.TotalSeconds:F1}s");
        slow.Cancelled.ShouldBe(3);
        run.Results.ShouldBe(3);
        finishedAfter.ShouldBeLessThan(TimeSpan.FromSeconds(15));   // not the 45 s the stopped instance would have taken
    }

    [Fact]
    public async Task ScatterGatherClient_WhenAskedForOneItem_ReturnsTheResponseOrTheRespondersErrors()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        var noData = new Error("Ping.NoData", "No data", ErrorType.NotFound);
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(
            perSecond: 100,
            burst: 10,
            respond: request => request.Value == "known" ? new PingResponse("pong") : noData);

        var answered = await requester.RequestAsync(new PingRequest("known"), TimeSpan.FromSeconds(10));
        var failed = await requester.RequestAsync(new PingRequest("unknown"), TimeSpan.FromSeconds(10));

        answered.Value.ShouldBe(new PingResponse("pong"));
        failed.Errors.ShouldBe([noData]);
    }

    [Fact]
    public async Task RateLimitedQueue_WhenAnInstanceStartsFailingBetweenHealthChecks_StepsAsideAtOnce()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        await using var requester = await StartRequesterAsync();
        // Both pods only check their health every 30 s; the broken one must not wait for that.
        var broken = false;
        await using var podA = await StartPodAsync(
            perSecond: 5,
            prefetch: 5,
            respond: request => Volatile.Read(ref broken) ? throw new InvalidOperationException("Upstream lost.") : new PingResponse("pong"),
            healthCheckInterval: TimeSpan.FromSeconds(30));
        await using var podB = await StartPodAsync(perSecond: 5, prefetch: 5, healthCheckInterval: TimeSpan.FromSeconds(30));

        var runId = await requester.StartRunAsync(items: 30);
        await WaitUntilAsync(() => podA.Handled.Count >= 3);
        Volatile.Write(ref broken, true);
        podA.MarkUnhealthy();
        var brokeAt = DateTime.UtcNow;
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        output.WriteLine($"pod-a {podA.Handled.Count} (after breaking: {podA.Handled.Count(x => x > brokeAt)}), pod-b {podB.Handled.Count}, gathered after {(run.GatheredAtUtc!.Value - brokeAt).TotalSeconds:F1}s");
        run.Results.ShouldBe(30);
        run.Unanswered.ShouldBe(0);
        // Without the early check, pod A would fail requests for up to 30 s.
        (run.GatheredAtUtc.Value - brokeAt).ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RateLimitedQueue_WhenARequestKeepsFailing_DropsItInsteadOfMovingItToTheErrorQueue()
    {
        // A long timeout: a copy moved to the error queue keeps the request's time-to-live, so it would
        // expire there too. Look at the error queue well before that.
        UseTimeout(TimeSpan.FromSeconds(60));
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(
            perSecond: 100,
            burst: 10,
            respond: _ => throw new InvalidOperationException("Always fails."));
        var errorQueueBefore = await ErrorQueueMessageCountAsync();

        await requester.StartRunAsync(items: 3);
        // Rebus tries each request 5 times before giving up on it.
        await WaitUntilAsync(() => pod.Handled.Count >= 15);
        await Task.Delay(TimeSpan.FromSeconds(1));

        var errorQueueAfter = await ErrorQueueMessageCountAsync();
        output.WriteLine($"error queue before: {errorQueueBefore}, after: {errorQueueAfter}, attempts {pod.Handled.Count}");
        errorQueueBefore.ShouldBeGreaterThanOrEqualTo(0);   // the queue exists, so a move would have shown
        errorQueueAfter.ShouldBe(errorQueueBefore);
        pod.Handled.Count.ShouldBe(15);
    }

    [Fact]
    public async Task ScatterGather_WhenTheRequesterStopsMidBatch_AnotherRequesterFinishesTheEvent()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        var database = bus.Postgres.CreateIsolatedConnectionString();
        var stopping = await RequesterHost.StartAsync(bus.RabbitMqConnection, database, _requestQueue);
        // A slow responder (4/s), so the batch is still waiting for replies when its requester goes away.
        await using var pod = await StartPodAsync(perSecond: 4);

        var runId = await stopping.StartRunAsync(items: 20);
        await WaitUntilAsync(() => pod.Handled.Count >= 3);
        // The requester stops mid-batch: its outbox transaction rolls back (as when its process dies and
        // Postgres drops the connection), so the event is still unprocessed and its row no longer claimed.
        await stopping.DisposeAsync();
        var stoppedAt = DateTime.UtcNow;

        await using var taking = await RequesterHost.StartAsync(bus.RabbitMqConnection, database, _requestQueue);
        var run = await taking.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(60));

        output.WriteLine($"finished {(run.GatheredAtUtc!.Value - stoppedAt).TotalSeconds:F1}s after the first requester stopped; responder handled {pod.Handled.Count}");
        run.Results.ShouldBe(20);
        run.Unanswered.ShouldBe(0);
        (await taking.PendingScatterMessagesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ScatterGatherRequest_WhenSentThroughTheMediator_HandlesWithTheGatheredReplies()
    {
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10);

        var answered = await requester.SendAsync(new CountPongsQuery(Items: 15));

        answered.ShouldBe(15);
    }

    [Fact]
    public async Task StreamingScatterGatherRequest_WhenTheHandlerHasEnough_StopsWaitingForTheRest()
    {
        await using var requester = await StartRequesterAsync();
        // 5 a second: all 20 would take 4 s.
        await using var pod = await StartPodAsync(perSecond: 5);
        var started = DateTime.UtcNow;

        var firstThree = await requester.SendAsync(new FirstPongsQuery(Items: 20, Wanted: 3, Timeout: TimeSpan.FromSeconds(30)));

        var took = DateTime.UtcNow - started;
        output.WriteLine($"first 3 of 20 after {took.TotalSeconds:F1}s");
        firstThree.Count.ShouldBe(3);
        took.ShouldBeLessThan(TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public async Task StreamingScatterGatherRequest_WhenNoReplyArrives_EndsAtTheTimeout()
    {
        await using var requester = await StartRequesterAsync();
        var started = DateTime.UtcNow;

        var replies = await requester.SendAsync(new FirstPongsQuery(Items: 5, Wanted: 5, Timeout: TimeSpan.FromSeconds(2)));

        var took = DateTime.UtcNow - started;
        replies.ShouldBeEmpty();
        took.ShouldBeInRange(TimeSpan.FromSeconds(1.8), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ScatterGather_BeforeAnyResponderHasStarted_RequestsWaitForIt()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        await using var requester = await StartRequesterAsync();

        var runId = await requester.StartRunAsync(items: 10);
        await Task.Delay(TimeSpan.FromSeconds(3));
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10);

        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));
        run.Results.ShouldBe(10);
    }

    [Fact]
    public async Task ScatterGather_AfterTheTimeout_RequestsExpireOnTheBus()
    {
        UseTimeout(TimeSpan.FromSeconds(2));
        await using var requester = await StartRequesterAsync();

        var runId = await requester.StartRunAsync(items: 10);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10);
        await Task.Delay(TimeSpan.FromSeconds(3));

        run.Unanswered.ShouldBe(10);
        pod.Handled.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfigurationStrategy_WhenSet_RunsOnEveryBus()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        var requesterStrategy = new RecordingBusConfigurationStrategy();
        var podStrategy = new RecordingBusConfigurationStrategy();
        await using var requester = await StartRequesterAsync(requesterStrategy);
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10, configuration: podStrategy);

        var runId = await requester.StartRunAsync(items: 5);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        run.Results.ShouldBe(5);
        // Requester: its own bus and the scatter-gather reply bus. Pod: its own (send-only) bus and the queue's.
        requesterStrategy.Transports.ShouldBe(2);
        requesterStrategy.Options.ShouldBe(2);
        podStrategy.Transports.ShouldBe(2);
        podStrategy.Options.ShouldBe(2);
    }

    [Fact]
    public async Task RateLimitedQueue_WithACustomRateLimiterStrategy_UsesItsLimiter()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        var rateLimiter = new CountingRateLimiterStrategy();
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10, rateLimiter: rateLimiter);

        var runId = await requester.StartRunAsync(items: 12);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        run.Results.ShouldBe(12);
        rateLimiter.Queues.ShouldBe([_requestQueue]);
        rateLimiter.Acquired.ShouldBe(12);
    }

    [Fact]
    public async Task Tracing_WhenItemsAreAnswered_PutsTheWholeBatchInTheOutboxMessagesTrace()
    {
        UseTimeout(TimeSpan.FromSeconds(30));
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is RebusDiagnosticConstants.ActivitySourceName or OutboxDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var requester = await StartRequesterAsync();
        await using var pod = await StartPodAsync(perSecond: 100, burst: 10);

        var runId = await requester.StartRunAsync(items: 5);
        var run = await requester.WaitUntilGatheredAsync(runId, TimeSpan.FromSeconds(30));

        var outbox = activities.Single(activity =>
            activity.Source.Name == OutboxDiagnostics.ActivitySourceName &&
            Equals(activity.GetTagItem("outbox.message.type"), typeof(PingRequested).FullName));
        var batch = activities.Where(activity => activity.TraceId == outbox.TraceId).ToList();
        var sends = batch.Where(activity => activity.Kind == ActivityKind.Client).ToList();
        var handlings = batch.Where(activity => activity.Kind == ActivityKind.Server).ToList();
        output.WriteLine($"trace {outbox.TraceId}: {sends.Count} sends, {handlings.Count} handlings");
        run.Results.ShouldBe(5);
        // Each item: the request sent and handled by the responder, then its reply sent and handled here.
        sends.Count.ShouldBe(10);
        handlings.Count.ShouldBe(10);
        handlings.ShouldAllBe(handling => sends.Any(send => send.SpanId == handling.ParentSpanId));
    }

    /// <summary>Messages in the <c>error</c> queue, or -1 when there is no such queue.</summary>
    private Task<long> ErrorQueueMessageCountAsync()
        => BrokerQueues.MessageCountAsync(bus.RabbitMqConnection, "error");

    private static void UseTimeout(TimeSpan timeout)
        => RequesterHost.PingScatterGatherHandler.CurrentTimeout = timeout;

    private Task<RequesterHost> StartRequesterAsync(IBusConfigurationStrategy? configuration = null)
        => RequesterHost.StartAsync(bus.RabbitMqConnection, bus.Postgres.CreateIsolatedConnectionString(), _requestQueue, configuration);

    private Task<ResponderPod> StartPodAsync(
        int perSecond,
        int burst = 1,
        int? prefetch = null,
        Func<PingRequest, Result<PingResponse>>? respond = null,
        IBusConfigurationStrategy? configuration = null,
        IRateLimiterStrategy? rateLimiter = null,
        TimeSpan? healthCheckInterval = null,
        TimeSpan? delay = null)
        => ResponderPod.StartAsync(bus.RabbitMqConnection, _requestQueue, perSecond, burst, prefetch, respond, configuration, rateLimiter, healthCheckInterval, delay);

    private static Error NoData(PingRequest request)
        => new("Ping.NoData", $"No data for {request.Value}", ErrorType.NotFound);

    private static int ItemNumber(PingRequest request)
        => int.Parse(request.Value["ping ".Length..], CultureInfo.InvariantCulture);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var giveUpAt = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > giveUpAt)
                throw new TimeoutException("Condition was not met within 30 s.");
            await Task.Delay(100);
        }
    }

    private static int BusiestSecond(IReadOnlyList<DateTime> handled)
    {
        var times = handled.Order().ToList();
        var busiest = 0;
        for (var start = 0; start < times.Count; start++)
        {
            var end = start;
            while (end < times.Count && times[end] - times[start] < TimeSpan.FromSeconds(1))
                end++;
            busiest = Math.Max(busiest, end - start);
        }
        return busiest;
    }
}
