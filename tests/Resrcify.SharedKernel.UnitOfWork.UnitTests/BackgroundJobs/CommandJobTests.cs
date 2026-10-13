using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Diagnostics;
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
public sealed class CommandJobTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly RecordingLogger<SendCommandJob<PingCommand>> _logger = new();
    private readonly SendCommandJob<PingCommand> _job;

    public CommandJobTests()
        => _job = new SendCommandJob<PingCommand>(_sender, _logger);

    [Fact]
    public async Task Execute_ShouldSendANewCommand_AndLogNothing_WhenItSucceeds()
    {
        Sends(Result.Success());

        await _job.Execute(Context());

        await _sender.Received(1).Send<Result>(Arg.Any<PingCommand>(), Arg.Any<CancellationToken>());
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_ShouldLogATransientFailureAtWarning()
    {
        Sends(Result.Failure(Error.ExternalFailure("Upstream.Down", "The upstream is down.")));

        await _job.Execute(Context());

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("Upstream.Down");
        entry.Message.ShouldContain("ping-job");
    }

    [Fact]
    public async Task Execute_ShouldLogAnExpectedAnswerAtInformation()
    {
        Sends(Result.Failure(Error.NotFound("Shard.NotFound", "No shard.")));

        await _job.Execute(Context());

        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task Execute_ShouldLetACancellationThrough_WithoutLoggingIt()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _sender
            .Send<Result>(Arg.Any<PingCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => _job.Execute(Context(), cancellation.Token).AsTask());

        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_ShouldLogAnExceptionAtError_AndThrowItAsAJobExecutionException()
    {
        var failure = new InvalidOperationException("broken");
        _sender.Send<Result>(Arg.Any<PingCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => _job.Execute(Context()).AsTask());

        thrown.InnerException.ShouldBeSameAs(failure);
        thrown.RefireImmediately.ShouldBeFalse();
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Execute_ShouldLogADebugLine_WhenTheMediatorLoggedTheExceptionAlready()
    {
        var failure = new InvalidOperationException("broken");
        LoggedExceptions.Claim(failure);   // as the mediator's behaviors do
        _sender.Send<Result>(Arg.Any<PingCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        await Should.ThrowAsync<JobExecutionException>(() => _job.Execute(Context()).AsTask());

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task Execute_ShouldLogTheSameExceptionAtErrorAgain_WhenALaterRunThrowsIt()
    {
        var cached = new InvalidOperationException("the cached client failed to start");
        _sender.Send<Result>(Arg.Any<PingCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(cached);

        Task RunAsync() => _job.Execute(Context()).AsTask();
        await Should.ThrowAsync<JobExecutionException>(RunAsync);
        await Should.ThrowAsync<JobExecutionException>(RunAsync);

        _logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Error, LogLevel.Error]);
    }

    [Fact]
    public void SendCommandJob_ShouldRefuseACommandReturningAValue_SentAsAResult()
        => Should.Throw<InvalidOperationException>(
                () => new SendCommandJob<CountCommand>(_sender, new RecordingLogger<SendCommandJob<CountCommand>>()))
            .Message.ShouldContain("CountCommand returns Result<Int32>");

    [Fact]
    public async Task SendCommandJob_ShouldSendACommandReturningAValue_AsItsOwnResultType()
    {
        _sender.Send<Result<int>>(Arg.Any<CountCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(3));
        var job = new SendCommandJob<CountCommand, Result<int>>(_sender, new RecordingLogger<SendCommandJob<CountCommand, Result<int>>>());

        await job.Execute(Context());

        await _sender.Received(1).Send<Result<int>>(Arg.Any<CountCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CommandJobs_ShouldNotOverlap_AsQuartzSeesThem()
        => JobBuilder.Create<SendCommandJob<PingCommand>>().Build().ConcurrentExecutionDisallowed.ShouldBeTrue();

    /// <summary>A command returning a value: an <c>IRequest&lt;Result&lt;int&gt;&gt;</c>, and so (covariance) an <c>IRequest&lt;Result&gt;</c> too.</summary>
    private sealed class CountCommand : ICommand<int>;

    private void Sends(Result result)
        => _sender.Send<Result>(Arg.Any<PingCommand>(), Arg.Any<CancellationToken>()).Returns(result);

    private static IJobExecutionContext Context()
    {
        var detail = Substitute.For<IJobDetail>();
        detail.Key.Returns(new JobKey("ping-job"));
        var context = Substitute.For<IJobExecutionContext>();
        context.JobDetail.Returns(detail);
        return context;
    }
}
