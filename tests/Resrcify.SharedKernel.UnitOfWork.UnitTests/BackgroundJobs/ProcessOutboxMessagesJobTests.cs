using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class ProcessOutboxMessagesJobTests
{
    private static readonly SystemTextJsonOutboxSerializer _serializer = new();

    [Fact]
    public async Task Execute_ShouldPublishEvents_AndMarkMessagesProcessed()
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        var messages = Enumerable
            .Range(0, 2)
            .Select(_ => CreateMessage(new TestDomainEvent(Guid.NewGuid(), "Test message")))
            .ToArray();
        await harness.SeedAsync(messages);

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10));

        // Assert
        await harness.Publisher
            .Received(messages.Length)
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());

        var rows = await harness.GetMessagesAsync();
        rows.Count.ShouldBe(messages.Length);
        rows.ShouldAllBe(m => m.ProcessedOnUtc != null);
        rows.ShouldAllBe(m => m.Error == null);
    }

    [Fact]
    public async Task Execute_ShouldRecordError_AndIncrementRetryCount_WhenPublishThrows()
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler failed")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "boom")));

        // Act — a throwing handler must not abort the job.
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10));

        // Assert — the row is left unprocessed (so it retries) with the error recorded.
        var row = (await harness.GetMessagesAsync()).Single();
        row.ProcessedOnUtc.ShouldBeNull();
        row.RetryCount.ShouldBe(1);
        row.Error.ShouldNotBeNull();
        row.Error.ShouldContain("handler failed");
    }

    [Fact]
    public async Task Execute_ShouldStopRetrying_OnceMaxRetryCountReached()
    {
        // Arrange
        var clock = new FakeTimeProvider(Now);
        await using var harness = await ClockedHarnessAsync(clock);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison")));

        // Act — run more cycles than the retry budget allows, each after the longest retry delay.
        const int maxRetryCount = 3;
        for (var cycle = 0; cycle < maxRetryCount + 2; cycle++)
        {
            await harness.Job.Execute(
                OutboxJobTestHarness.JobContext(batchSize: 10, maxRetryCount: maxRetryCount));
            clock.Advance(TimeSpan.FromMinutes(5));
        }

        // Assert — the message is attempted exactly maxRetryCount times, then marked given up and skipped.
        await harness.Publisher
            .Received(maxRetryCount)
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());

        var row = (await harness.GetMessagesAsync()).Single();
        row.RetryCount.ShouldBe(maxRetryCount);
        row.ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
        row.Error.ShouldNotBeNull();
        row.Error.ShouldStartWith("Gave up at ");
        row.Error.ShouldContain("always fails");
    }

    [Fact]
    public async Task Execute_ShouldNotTryAGivenUpMessageAgain_WhenTheRetryLimitIsRaised()
    {
        // Arrange — the message gives up after one try.
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison")));
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10, maxRetryCount: 1));

        // Act — a limit it is under: an unmarked message would be tried again.
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 10, maxRetryCount: 5));

        // Assert
        await harness.Publisher
            .Received(1)
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        (await harness.GetMessagesAsync()).Single().ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
    }

    /// <summary>
    /// Quartz 4 removed the typed JobDataMap getters, so the job now reads the stored object itself. These
    /// pin how it treats each shape a batch size can arrive in: honoured when it is a usable number however
    /// it was stored, and the default of 20 otherwise - never an exception that fails the job. A run drains the
    /// outbox batch by batch, so the batch size shows in how many reads the 5 messages took.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizeShapes))]
    public async Task Execute_ShouldReadBatchesOfTheStoredSize_HoweverItWasStored(object? storedBatchSize, int expectedReads)
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync();
        await harness.SeedAsync(Enumerable
            .Range(0, 5)
            .Select(_ => CreateMessage(new TestDomainEvent(Guid.NewGuid(), "Test message")))
            .ToArray());
        var data = new JobDataMap();
        if (storedBatchSize is not null)
            data.Add("ProcessBatchSize", storedBatchSize);
        var readsBefore = harness.Queries.Selects;

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(data));

        // Assert
        (harness.Queries.Selects - readsBefore).ShouldBe(expectedReads);
        (await harness.GetMessagesAsync()).ShouldAllBe(m => m.ProcessedOnUtc != null);
    }

    public static TheoryData<object?, int> BatchSizeShapes => new()
    {
        { 2, 3 },               // stored as an int, as the setup does: batches of 2, 2 and 1
        { 2L, 3 },              // a long, as a persistent store may hand it back
        { "2", 3 },             // a string, as configuration supplies it
        { "not-a-number", 1 },  // unusable: falls back to the default batch of 20
        { long.MaxValue, 1 },   // out of int range: falls back rather than overflowing
        { null, 1 },            // absent: the default batch of 20
    };

    [Fact]
    public async Task Execute_ShouldDrainTheBacklog_WhileItsBatchesComeBackFull()
    {
        // Arrange — 70 waiting, 20 a batch: four batches (20, 20, 20, 10) in one run.
        await using var harness = await OutboxJobTestHarness.CreateAsync(new FakeTimeProvider());
        await harness.SeedAsync(Messages(70));

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20));

        // Assert
        (await harness.GetMessagesAsync()).ShouldAllBe(m => m.ProcessedOnUtc != null);
        await harness.Publisher.Received(70).Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ShouldStopStartingBatches_AfterEightyPercentOfTheInterval()
    {
        // Arrange — every message takes a second; the interval is 30 s, so batches start for 24 s.
        var clock = new FakeTimeProvider();
        await using var harness = await OutboxJobTestHarness.CreateAsync(clock);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                return Task.CompletedTask;
            });
        await harness.SeedAsync(Messages(70));

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20, intervalInSeconds: 30));

        // Assert — the first batch ended at 20 s (within 24 s: another one starts); the second at 40 s (stop).
        var rows = await harness.GetMessagesAsync();
        rows.Count(m => m.ProcessedOnUtc != null).ShouldBe(40);
        rows.Count(m => m.ProcessedOnUtc == null).ShouldBe(30);
    }

    [Fact]
    public async Task Execute_ShouldReadAQuietOutboxOnce()
    {
        // Arrange
        await using var harness = await OutboxJobTestHarness.CreateAsync(new FakeTimeProvider());

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20));

        // Assert
        harness.Queries.Selects.ShouldBe(1);
    }

    [Fact]
    public async Task Execute_ShouldNotTryAFailedMessageAgain_InTheSameRun()
    {
        // Arrange — the first of 25 messages always fails; batches of 20 keep coming back full.
        await using var harness = await OutboxJobTestHarness.CreateAsync(new FakeTimeProvider());
        var messages = Messages(25);
        var poison = messages[0];
        harness.Publisher
            .Publish(Arg.Is<IDomainEvent>(e => ((TestDomainEvent)e).Message == "poison"), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        poison.Content = _serializer.Serialize(new TestDomainEvent(Guid.NewGuid(), "poison"));
        await harness.SeedAsync(messages);

        // Act
        await harness.Job.Execute(OutboxJobTestHarness.JobContext(batchSize: 20, maxRetryCount: 3));

        // Assert — the rest drained; the failed one was tried once, and waits for the next run.
        var rows = await harness.GetMessagesAsync();
        rows.Count(m => m.ProcessedOnUtc != null).ShouldBe(24);
        rows.Single(m => m.Id == poison.Id).RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task Execute_ShouldSpaceAFailingMessagesTries_ByTheBackoff()
    {
        // A downstream outage: the handler fails every try. The tries wait 5 s, then 10 s, instead of one per run. A
        // transient failure gets as many tries as any other here (MaxTransientRetryCount = MaxRetryCount, as before 4.3.0).
        var clock = new FakeTimeProvider(Now);
        await using var harness = await ClockedHarnessAsync(clock);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("the downstream service didn't answer")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "outage")));

        var tries = new int[6];
        var run = () => RunAsync(harness, maxTransientRetryCount: 3);
        await run();                                                // 0 s: the first try fails
        tries[0] = await TriesAsync(harness);
        (await harness.GetMessagesAsync()).Single().NextAttemptOnUtc.ShouldBe(Now.UtcDateTime.AddSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(4));
        await run();                                                // 4 s: not due
        tries[1] = await TriesAsync(harness);
        clock.Advance(TimeSpan.FromSeconds(1));
        await run();                                                // 5 s: the second try fails
        tries[2] = await TriesAsync(harness);
        (await harness.GetMessagesAsync()).Single().NextAttemptOnUtc.ShouldBe(Now.UtcDateTime.AddSeconds(15));
        clock.Advance(TimeSpan.FromSeconds(9));
        await run();                                                // 14 s: not due
        tries[3] = await TriesAsync(harness);
        clock.Advance(TimeSpan.FromSeconds(1));
        await run();                                                // 15 s: the third try fails, and it gives up
        tries[4] = await TriesAsync(harness);
        clock.Advance(TimeSpan.FromHours(1));
        await run();
        tries[5] = await TriesAsync(harness);

        tries.ShouldBe([1, 1, 2, 2, 3, 3]);
        (await harness.GetMessagesAsync()).Single().ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
    }

    [Fact]
    public async Task Execute_ShouldProcessAMessage_OnceItsNextTryIsDue()
    {
        var clock = new FakeTimeProvider(Now);
        await using var harness = await ClockedHarnessAsync(clock);
        var waiting = CreateMessage(new TestDomainEvent(Guid.NewGuid(), "waiting"));
        waiting.RetryCount = 1;
        waiting.NextAttemptOnUtc = Now.UtcDateTime.AddSeconds(30);
        var due = CreateMessage(new TestDomainEvent(Guid.NewGuid(), "due"));
        due.RetryCount = 1;
        due.NextAttemptOnUtc = Now.UtcDateTime.AddSeconds(-1);
        await harness.SeedAsync(waiting, due);

        await RunAsync(harness);
        var afterFirstRun = await harness.GetMessagesAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        await RunAsync(harness);

        afterFirstRun.Single(m => m.Id == due.Id).ProcessedOnUtc.ShouldNotBeNull();
        afterFirstRun.Single(m => m.Id == waiting.Id).ProcessedOnUtc.ShouldBeNull();
        (await harness.GetMessagesAsync()).ShouldAllBe(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc != OutboxMessage.GivenUpProcessedOnUtc);
    }

    [Fact]
    public async Task Execute_ShouldWaitTheConfiguredDelays_UpToTheLongest()
    {
        var clock = new FakeTimeProvider(Now);
        await using var harness = await ClockedHarnessAsync(clock);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison")));
        var context = OutboxJobTestHarness.JobContext(
            maxRetryCount: 10,
            retryDelay: TimeSpan.FromSeconds(30),
            maxRetryDelay: TimeSpan.FromSeconds(45));

        var waits = new TimeSpan[3];
        for (var attempt = 0; attempt < waits.Length; attempt++)
        {
            await harness.Job.Execute(context);
            var next = (await harness.GetMessagesAsync()).Single().NextAttemptOnUtc.ShouldNotBeNull();
            waits[attempt] = next - clock.GetUtcNow().UtcDateTime;
            clock.Advance(waits[attempt]);
        }

        waits.ShouldBe([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45)]);
    }

    [Fact]
    public async Task Execute_ShouldTryAgainInTheNextRun_WhenTheRetryDelayIsZero()
    {
        await using var harness = await ClockedHarnessAsync(new FakeTimeProvider(Now));
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("always fails")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison")));
        var context = OutboxJobTestHarness.JobContext(maxRetryCount: 3, retryDelay: TimeSpan.Zero);

        await harness.Job.Execute(context);
        await harness.Job.Execute(context);

        var row = (await harness.GetMessagesAsync()).Single();
        row.RetryCount.ShouldBe(2);
        row.NextAttemptOnUtc.ShouldBeNull();
    }

    [Fact]
    public async Task Execute_ShouldLogEachFailedTryOnce_AndTheGiveUpAtError()
    {
        var clock = new FakeTimeProvider(Now);
        using var logs = new RecordingLoggerProvider();
        await using var harness = await ClockedHarnessAsync(clock, logs);
        var failure = new InvalidOperationException("the handler's bug");
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        var message = CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison"));
        await harness.SeedAsync(message);

        for (var run = 0; run < 3; run++)
        {
            await RunAsync(harness, maxRetryCount: 2);
            clock.Advance(TimeSpan.FromMinutes(5));
        }

        var logged = logs.Outbox();
        logged.Count.ShouldBe(2);
        logged[0].Level.ShouldBe(LogLevel.Error);
        logged[0].Message.ShouldBe(
            $"Outbox message {message.Id} (TestDomainEvent, TestDbContext) failed try 1 of 2; the next try waits "
            + "00:00:05: InvalidOperationException: the handler's bug");
        logged[0].Exception.ShouldBeSameAs(failure);
        logged[1].Level.ShouldBe(LogLevel.Error);
        logged[1].Message.ShouldBe(
            $"Outbox message {message.Id} (TestDomainEvent, TestDbContext) gave up after try 2 (non-transient failures "
            + "get 2 tries); it is kept as poison: InvalidOperationException: the handler's bug");
        logged[1].Exception.ShouldBeSameAs(failure);
        LoggedExceptions.IsLogged(failure).ShouldBeFalse();
    }

    [Fact]
    public async Task Execute_ShouldLogATransientFailedTryAtWarning()
    {
        using var logs = new RecordingLoggerProvider();
        await using var harness = await ClockedHarnessAsync(new FakeTimeProvider(Now), logs);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("connection refused")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "outage")));

        await RunAsync(harness);

        var logged = logs.Outbox().ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Warning);
        logged.Message.ShouldContain("failed try 1 of 12");
        logged.Exception.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Execute_ShouldNotLogTheStackAgain_WhenAHandlerLoggedTheExceptionAlready()
    {
        // As the mediator's unit-of-work behavior does around a command the handler sent.
        using var logs = new RecordingLoggerProvider();
        await using var harness = await ClockedHarnessAsync(new FakeTimeProvider(Now), logs);
        var failure = new InvalidOperationException("logged by the handler");
        LoggedExceptions.Claim(failure).ShouldBeTrue();
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "logged")));

        await RunAsync(harness);

        var logged = logs.Outbox().ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Exception.ShouldBeNull();
        logged.Message.ShouldEndWith("InvalidOperationException: logged by the handler");
        LoggedExceptions.IsLogged(failure).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Execute_ShouldTryATransientlyFailingMessageTwelveTimes_WithBackoff_ThenGiveUp(bool http)
    {
        // A downstream outage longer than 3 tries: the default limits (3, and 12 for a transient failure).
        Exception failure = http
            ? new HttpRequestException("the downstream service is down", null, HttpStatusCode.ServiceUnavailable)
            : new TimeoutException("the downstream service didn't answer");
        var clock = new FakeTimeProvider(Now);
        using var logs = new RecordingLoggerProvider();
        await using var harness = await ClockedHarnessAsync(clock, logs);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "outage")));

        var waits = new List<TimeSpan>();
        for (var run = 0; run < 20; run++)
        {
            await RunAsync(harness);
            var row = (await harness.GetMessagesAsync()).Single();
            if (row.ProcessedOnUtc is not null)
                break;
            var wait = row.NextAttemptOnUtc.ShouldNotBeNull() - clock.GetUtcNow().UtcDateTime;
            waits.Add(wait);
            clock.Advance(wait);
        }
        clock.Advance(TimeSpan.FromHours(1));
        await RunAsync(harness);

        await harness.Publisher.Received(12).Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        waits.Select(wait => (int)wait.TotalSeconds).ShouldBe([5, 10, 20, 40, 80, 160, 300, 300, 300, 300, 300]);
        var givenUp = (await harness.GetMessagesAsync()).Single();
        givenUp.RetryCount.ShouldBe(12);
        givenUp.ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
        givenUp.Error.ShouldNotBeNull().ShouldStartWith("Gave up at ");
        givenUp.Error.ShouldContain("after 12 tries.");
        var logged = logs.Outbox();
        logged.Count.ShouldBe(12);
        logged.Take(11).ShouldAllBe(line => line.Level == LogLevel.Warning);
        logged[10].Message.ShouldContain("failed try 11 of 12");
        logged[11].Level.ShouldBe(LogLevel.Error);
        logged[11].Message.ShouldContain("gave up after try 12 (transient failures get 12 tries)");
    }

    [Fact]
    public async Task Execute_ShouldGiveUpANonTransientlyFailingMessage_AfterThreeTries()
    {
        var clock = new FakeTimeProvider(Now);
        await using var harness = await ClockedHarnessAsync(clock);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("the handler's bug")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "poison")));

        for (var run = 0; run < 5; run++)
        {
            await RunAsync(harness);
            clock.Advance(TimeSpan.FromMinutes(5));
        }

        await harness.Publisher.Received(3).Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        var row = (await harness.GetMessagesAsync()).Single();
        row.RetryCount.ShouldBe(3);
        row.ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
    }

    [Fact]
    public async Task Execute_ShouldGiveUpAtOnce_WhenANonTransientFailureFollowsTransientRetriesPastTheLimit()
    {
        // Five transient failures (past the 3 tries a non-transient one gets), then the handler's bug: no more tries.
        var clock = new FakeTimeProvider(Now);
        using var logs = new RecordingLoggerProvider();
        await using var harness = await ClockedHarnessAsync(clock, logs);
        var tries = 0;
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++tries <= 5
                ? Task.FromException(new HttpRequestException("the downstream service is down", null, HttpStatusCode.BadGateway))
                : Task.FromException(new InvalidOperationException("the handler's bug")));
        await harness.SeedAsync(CreateMessage(new TestDomainEvent(Guid.NewGuid(), "outage, then a bug")));

        for (var run = 0; run < 10; run++)
        {
            await RunAsync(harness);
            clock.Advance(TimeSpan.FromMinutes(5));
        }

        tries.ShouldBe(6);
        var row = (await harness.GetMessagesAsync()).Single();
        row.RetryCount.ShouldBe(6);
        row.ProcessedOnUtc.ShouldBe(OutboxMessage.GivenUpProcessedOnUtc);
        row.Error.ShouldNotBeNull().ShouldStartWith("Gave up at ");
        row.Error.ShouldContain("after 6 tries.");
        row.Error.ShouldContain("the handler's bug");
        var gaveUp = logs.Outbox()[^1];
        gaveUp.Level.ShouldBe(LogLevel.Error);
        gaveUp.Message.ShouldContain("gave up after try 6 (non-transient failures get 3 tries)");
    }

    [Fact]
    public async Task Execute_ShouldReadAMessageRetryingTransientlyPastMaxRetryCount()
    {
        var clock = new FakeTimeProvider(Now);
        await using var harness = await ClockedHarnessAsync(clock);
        var retrying = CreateMessage(new TestDomainEvent(Guid.NewGuid(), "after an outage"));
        retrying.RetryCount = 7;
        retrying.NextAttemptOnUtc = Now.UtcDateTime.AddSeconds(-1);
        await harness.SeedAsync(retrying);

        await RunAsync(harness, maxRetryCount: 3);

        await harness.Publisher.Received(1).Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        var row = (await harness.GetMessagesAsync()).Single();
        row.ProcessedOnUtc.ShouldNotBeNull();
        row.ProcessedOnUtc.ShouldNotBe(OutboxMessage.GivenUpProcessedOnUtc);
    }

    [Fact]
    public async Task Execute_ShouldGiveATransientFailureTheOtherLimit_WhenTheJobWasStoredWithAHigherOneOnly()
    {
        // A job stored without the transient limit, and a MaxRetryCount above its default of 12: 13 tries for both.
        var clock = new FakeTimeProvider(Now);
        await using var harness = await ClockedHarnessAsync(clock);
        harness.Publisher
            .Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("the downstream service didn't answer")));
        var retrying = CreateMessage(new TestDomainEvent(Guid.NewGuid(), "outage"));
        retrying.RetryCount = 11;
        await harness.SeedAsync(retrying);

        await RunAsync(harness, maxRetryCount: 13);

        var row = (await harness.GetMessagesAsync()).Single();
        row.RetryCount.ShouldBe(12);
        row.ProcessedOnUtc.ShouldBeNull();
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    // The job reads the due messages on this clock, and the processor writes the next try's time on the container's.
    private static Task<OutboxJobTestHarness> ClockedHarnessAsync(
        FakeTimeProvider clock,
        RecordingLoggerProvider? logs = null)
        => OutboxJobTestHarness.CreateAsync(clock, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            if (logs is not null)
                services.AddLogging(logging => logging.AddProvider(logs));
        });

    private static Task RunAsync(
        OutboxJobTestHarness harness,
        int maxRetryCount = 3,
        int? maxTransientRetryCount = null)
        => harness.Job
            .Execute(OutboxJobTestHarness.JobContext(
                batchSize: 10,
                maxRetryCount: maxRetryCount,
                maxTransientRetryCount: maxTransientRetryCount))
            .AsTask();

    private static async Task<int> TriesAsync(OutboxJobTestHarness harness)
        => (await harness.GetMessagesAsync()).Single().RetryCount;

    private static OutboxMessage[] Messages(int count)
    {
        var start = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        return [.. Enumerable
            .Range(0, count)
            .Select(index =>
            {
                var message = CreateMessage(new TestDomainEvent(Guid.NewGuid(), $"message {index}"));
                message.OccurredOnUtc = start.AddSeconds(index);
                return message;
            })];
    }

    private static OutboxMessage CreateMessage(IDomainEvent domainEvent)
        => new()
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = domainEvent.GetType().FullName!,
            Content = _serializer.Serialize(domainEvent),
        };
}
