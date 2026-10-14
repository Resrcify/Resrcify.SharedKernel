using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class AsyncCommandJobTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly RecordingLogger<PingWhenAskedJob> _logger = new();

    [Fact]
    public async Task Execute_ShouldSendTheCommandItMade_AndLogNothing_WhenItSucceeds()
    {
        var command = new PingCommand();
        _sender.Send<Result>(command, Arg.Any<CancellationToken>()).Returns(Result.Success());
        var job = new PingWhenAskedJob(_sender, _logger, _ => ValueTask.FromResult<PingCommand?>(command));

        await job.Execute(Context());

        await _sender.Received(1).Send<Result>(command, Arg.Any<CancellationToken>());
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_ShouldSkipTheRun_AndSayItAtDebug_WhenThereIsNoCommand()
    {
        var job = new PingWhenAskedJob(_sender, _logger, _ => ValueTask.FromResult<PingCommand?>(null));

        await job.Execute(Context());

        await _sender.DidNotReceiveWithAnyArgs().Send<Result>(default!, default);
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Message.ShouldBe("Job ping-job had no PingCommand to send this run; skipped");
    }

    [Fact]
    public async Task Execute_ShouldPassTheRunsCancellationToken_ToCreateCommandAsync()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken seen = default;
        var job = new PingWhenAskedJob(_sender, _logger, token =>
        {
            seen = token;
            return ValueTask.FromResult<PingCommand?>(null);
        });

        await job.Execute(Context(), cancellation.Token);

        seen.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task Execute_ShouldLogAFailedResult_AsCommandJobDoes()
    {
        _sender
            .Send<Result>(Arg.Any<PingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.ExternalFailure("Upstream.Down", "The upstream is down.")));
        var job = new PingWhenAskedJob(_sender, _logger, _ => ValueTask.FromResult<PingCommand?>(new PingCommand()));

        await job.Execute(Context());

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("Upstream.Down");
    }

    [Fact]
    public async Task Execute_ShouldLogAnExceptionFromCreateCommandAsyncAtError_AndThrowItAsAJobExecutionException()
    {
        var failure = new InvalidOperationException("the database is down");
        var job = new PingWhenAskedJob(_sender, _logger, _ => ValueTask.FromException<PingCommand?>(failure));

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => job.Execute(Context()).AsTask());

        thrown.InnerException.ShouldBeSameAs(failure);
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Execute_ShouldLetACancellationWhileMakingTheCommandThrough_WithoutLoggingIt()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var job = new PingWhenAskedJob(
            _sender,
            _logger,
            token => ValueTask.FromException<PingCommand?>(new OperationCanceledException(token)));

        await Should.ThrowAsync<OperationCanceledException>(() => job.Execute(Context(), cancellation.Token).AsTask());

        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void AsyncCommandJob_ShouldRefuseACommandReturningAValue_SentAsAResult()
        => Should.Throw<InvalidOperationException>(() => new CountWhenAskedJob(_sender))
            .Message.ShouldContain("CountCommand returns Result<Int32>");

    [Fact]
    public void AsyncCommandJobs_ShouldNotOverlap_AsQuartzSeesThem()
        => JobBuilder.Create<PingWhenAskedJob>().Build().ConcurrentExecutionDisallowed.ShouldBeTrue();

    [Fact]
    public async Task ASkippedRun_ShouldBeatAsAHealthyHeartbeat()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_sender);
        services.AddSingleton(_logger);
        services.AddSingleton<Func<CancellationToken, ValueTask<PingCommand?>>>(_ => ValueTask.FromResult<PingCommand?>(null));
        services.AddSingleton<RunFinished>();
        services.AddQuartz(quartz => quartz
            .AddJobHeartbeats()
            .AddJobListener(provider => provider.GetRequiredService<RunFinished>())
            .AddJob<PingWhenAskedJob>(job => job.WithIdentity(Key).StoreDurably()));
        await using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        try
        {
            await scheduler.TriggerJob(Key);
            var exception = await provider.GetRequiredService<RunFinished>().Task.WaitAsync(TimeSpan.FromSeconds(10));

            exception.ShouldBeNull();
            provider.GetRequiredService<JobHeartbeatRegistry>().LastBeat(Key).ShouldNotBeNull();
            await _sender.DidNotReceiveWithAnyArgs().Send<Result>(default!, default);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    private static readonly JobKey Key = new("ping-job");

    private static IJobExecutionContext Context()
    {
        var detail = Substitute.For<IJobDetail>();
        detail.Key.Returns(Key);
        var context = Substitute.For<IJobExecutionContext>();
        context.JobDetail.Returns(detail);
        return context;
    }

    /// <summary>A command returning a value: an <c>IRequest&lt;Result&lt;int&gt;&gt;</c>, and so (covariance) an <c>IRequest&lt;Result&gt;</c> too.</summary>
    private sealed class CountCommand : ICommand<int>;

    private sealed class CountWhenAskedJob(ISender sender)
        : AsyncCommandJob<CountCommand>(sender, new RecordingLogger<CountWhenAskedJob>())
    {
        protected override ValueTask<CountCommand?> CreateCommandAsync(
            IJobExecutionContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<CountCommand?>(null);
    }

    /// <summary>Completes with the outcome of the first run that finishes.</summary>
    private sealed class RunFinished : IJobListener
    {
        private readonly TaskCompletionSource<JobExecutionException?> _finished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => nameof(RunFinished);

        public Task<JobExecutionException?> Task => _finished.Task;

        public ValueTask JobToBeExecuted(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask JobExecutionVetoed(
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            _finished.TrySetResult(jobException);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>An async command job whose command (or none) comes from a delegate.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Built by Quartz from DI in a test, which needs it reachable; kept next to its tests.")]
internal sealed class PingWhenAskedJob(
    ISender sender,
    RecordingLogger<PingWhenAskedJob> logger,
    Func<CancellationToken, ValueTask<PingCommand?>> create)
    : AsyncCommandJob<PingCommand>(sender, logger)
{
    protected override ValueTask<PingCommand?> CreateCommandAsync(
        IJobExecutionContext context,
        CancellationToken cancellationToken)
        => create(cancellationToken);
}
