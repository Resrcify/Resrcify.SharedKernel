using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Configuration;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

/// <summary>
/// The non-concurrent behavior in the mediator, each request sent in a scope of its own (as an endpoint and a job send
/// them). Every handler waits until the test lets it finish, so what runs when is decided by the test, never by a clock.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class NonConcurrentPipelineBehaviorTests
    : IAsyncLifetime
{
    private readonly Journal _journal = new();
    private ServiceProvider? _services;

    private ServiceProvider Provider
        => _services ?? throw new InvalidOperationException("Start the services first.");

    public Task InitializeAsync()
        => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
    }

    [Fact]
    public async Task TwoRunsWithTheSameKey_ShouldRunOneAfterTheOther()
    {
        Start(cfg => cfg.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>)));
        var first = new RunJob("first", "shard-1");
        var second = new RunJob("second", "shard-1");

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);

        second.Run.Started.ShouldBeFalse();
        secondSend.IsCompleted.ShouldBeFalse();
        first.Run.Finish();
        (await firstSend).IsSuccess.ShouldBeTrue();
        await second.Run.HasStarted;
        second.Run.Finish();
        (await secondSend).IsSuccess.ShouldBeTrue();
        _journal.Entries.ShouldBe(["first started", "first finished", "second started", "second finished"]);
        Locks().Count.ShouldBe(0);
    }

    [Fact]
    public async Task RunsWithDifferentKeys_ShouldRunSideBySide()
    {
        Start(cfg => cfg.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>)));
        var first = new RunJob("first", "shard-1");
        var second = new RunJob("second", "shard-2");

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);

        await second.Run.HasStarted;
        firstSend.IsCompleted.ShouldBeFalse();
        second.Run.Finish();
        (await secondSend).IsSuccess.ShouldBeTrue();
        first.Run.Finish();
        (await firstSend).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task RunsWithoutAKey_ShouldRunOneAtATimePerRequestType()
    {
        Start(cfg => cfg.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>)));
        var first = new RunOtherJob("first");
        var second = new RunOtherJob("second");
        var anotherType = new RunJob("another type", Key: null);

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);
        var anotherTypeSend = SendAsync(anotherType);

        await anotherType.Run.HasStarted;   // another request type: side by side
        second.Run.Started.ShouldBeFalse(); // the same request type: waits
        anotherType.Run.Finish();
        first.Run.Finish();
        await Task.WhenAll(firstSend, anotherTypeSend);
        await second.Run.HasStarted;
        second.Run.Finish();
        (await secondSend).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ARunWithAnEmptyKey_ShouldWaitForOneWithout()
    {
        Start(cfg => cfg.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>)));
        var first = new RunJob("first", Key: null);
        var second = new RunJob("second", Key: string.Empty);

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);

        second.Run.Started.ShouldBeFalse();
        first.Run.Finish();
        await firstSend;
        await second.Run.HasStarted;
        second.Run.Finish();
        await secondSend;
    }

    [Fact]
    public async Task ARunCancelledWhileWaiting_ShouldNotBeHandled_NorKeepTheLock()
    {
        Start(cfg => cfg.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>)));
        var first = new RunJob("first", "shard-1");
        var cancelled = new RunJob("cancelled", "shard-1");
        var third = new RunJob("third", "shard-1");
        using var cancellation = new CancellationTokenSource();

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var cancelledSend = SendAsync(cancelled, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => cancelledSend);
        cancelled.Run.Started.ShouldBeFalse();
        first.Run.Finish();
        await firstSend;
        third.Run.Finish();
        (await SendAsync(third)).IsSuccess.ShouldBeTrue();
        _journal.Entries.ShouldNotContain("cancelled started");
        Locks().Count.ShouldBe(0);
    }

    [Fact]
    public async Task AnExceptionInTheFirstRun_ShouldReleaseTheLock()
    {
        Start(cfg => cfg.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>)));
        var first = new RunJob("first", "shard-1");
        var second = new RunJob("second", "shard-1");

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);
        first.Run.Fail();

        await Should.ThrowAsync<InvalidOperationException>(() => firstSend);
        await second.Run.HasStarted;
        second.Run.Finish();
        (await secondSend).IsSuccess.ShouldBeTrue();
        Locks().Count.ShouldBe(0);
    }

    [Fact]
    public async Task ARequestThatIsNotNonConcurrent_ShouldNotGetTheBehavior()
    {
        Start(cfg => cfg.AddOpenBehavior(typeof(NonConcurrentPipelineBehavior<,>)));
        await using (var scope = Provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetServices<IPipelineBehavior<RunPlain, Result>>().ShouldBeEmpty();
            scope.ServiceProvider.GetServices<IPipelineBehavior<RunJob, Result>>()
                .ShouldHaveSingleItem()
                .ShouldBeOfType<NonConcurrentPipelineBehavior<RunJob, Result>>();
        }

        var first = new RunPlain("first");
        var second = new RunPlain("second");

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);

        await second.Run.HasStarted;
        first.Run.Finish();
        second.Run.Finish();
        await Task.WhenAll(firstSend, secondSend);
    }

    [Fact]
    public async Task TheStandardBehaviors_ShouldRunTwoRunsWithTheSameKeyOneAfterTheOther()
    {
        Start(cfg => cfg.AddStandardBehaviors());
        var first = new RunJob("first", "shard-1");
        var second = new RunJob("second", "shard-1");

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);

        second.Run.Started.ShouldBeFalse();
        first.Run.Finish();
        await firstSend;
        await second.Run.HasStarted;
        second.Run.Finish();
        (await secondSend).IsSuccess.ShouldBeTrue();
        _journal.Entries.ShouldBe(["first started", "first finished", "second started", "second finished"]);
    }

    [Fact]
    public async Task TheStandardBehaviorsWithoutIt_ShouldRunTwoRunsWithTheSameKeySideBySide()
    {
        Start(cfg => cfg.AddStandardBehaviors(standard => standard
            .Without(StandardBehavior.NonConcurrent)));
        var first = new RunJob("first", "shard-1");
        var second = new RunJob("second", "shard-1");

        var firstSend = SendAsync(first);
        await first.Run.HasStarted;
        var secondSend = SendAsync(second);

        await second.Run.HasStarted;
        firstSend.IsCompleted.ShouldBeFalse();
        first.Run.Finish();
        second.Run.Finish();
        await Task.WhenAll(firstSend, secondSend);
    }

    [Fact]
    public void BehaviorCheck_ShouldFailTheRegistration_WhenTheBehaviorIsLeftOutButARequestIsNonConcurrent()
        => Should.Throw<InvalidOperationException>(() => BehaviorCheck.RunFor(
                new ServiceCollection(),
                [(typeof(RunJob), BehaviorCheck.PipelineKind.Task)],
                new MediatorConfiguration()
                    .AddStandardBehaviors(standard => standard.Without(StandardBehavior.NonConcurrent))
                    .OpenBehaviorTypes))
            .Message.ShouldContain("NonConcurrentPipelineBehavior");

    [Fact]
    public void BehaviorCheck_ShouldPass_WithTheStandardBehaviors()
        => Should.NotThrow(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [(typeof(RunJob), BehaviorCheck.PipelineKind.Task)],
            new MediatorConfiguration().AddStandardBehaviors().OpenBehaviorTypes));

    private async Task<Result> SendAsync(
        IRequest<Result> request,
        CancellationToken cancellationToken = default)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
    }

    private NonConcurrentLocks Locks()
        => Provider.GetRequiredService<NonConcurrentLocks>();

    private void Start(Action<MediatorConfiguration> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_journal);
        services.AddScoped(_ => Substitute.For<IUnitOfWork>().RunningOperations<Result>());
        services.AddMediator(configure);
        // Open generic handlers, closed here: the assembly scan of other tests must not find a non-concurrent request.
        services.AddTransient<IRequestHandler<RunJob, Result>, RunningHandler<RunJob>>();
        services.AddTransient<IRequestHandler<RunOtherJob, Result>, RunningHandler<RunOtherJob>>();
        services.AddTransient<IRequestHandler<RunPlain, Result>, RunningHandler<RunPlain>>();
        _services = services.BuildServiceProvider();
    }

    /// <summary>A run of a job the test controls: it waits, once started, until the test finishes or fails it.</summary>
    public sealed class Run
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HasStarted
            => _started.Task;

        public bool Started
            => _started.Task.IsCompleted;

        public void Finish()
            => _finish.TrySetResult();

        public void Fail()
            => _finish.TrySetException(new InvalidOperationException("The job failed."));

        internal Task StartAsync()
        {
            _started.TrySetResult();
            return _finish.Task;
        }
    }

    public interface IRun
    {
        string Name { get; }

        Run Run { get; }
    }

    /// <summary>A job command with a concurrency key (per shard, say).</summary>
    public sealed record RunJob(string Name, string? Key) : ICommand, INonConcurrentRequest, IRun
    {
        public Run Run { get; } = new();

        public string? ConcurrencyKey
            => Key;
    }

    /// <summary>A job command without one: every run of it one at a time.</summary>
    public sealed record RunOtherJob(string Name) : ICommand, INonConcurrentRequest, IRun
    {
        public Run Run { get; } = new();
    }

    /// <summary>A command that isn't non-concurrent.</summary>
    public sealed record RunPlain(string Name) : ICommand, IRun
    {
        public Run Run { get; } = new();
    }

    /// <summary>What the handlers did, in order.</summary>
    public sealed class Journal
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public string[] Entries
            => [.. _entries];

        public void Write(string entry)
            => _entries.Enqueue(entry);
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Resolved from the container.")]
    internal sealed class RunningHandler<TRequest>(Journal journal) : IRequestHandler<TRequest, Result>
        where TRequest : IRequest<Result>, IRun
    {
        public async Task<Result> Handle(TRequest request, CancellationToken cancellationToken)
        {
            journal.Write($"{request.Name} started");
            await request.Run.StartAsync();
            journal.Write($"{request.Name} finished");
            return Result.Success();
        }
    }
}
