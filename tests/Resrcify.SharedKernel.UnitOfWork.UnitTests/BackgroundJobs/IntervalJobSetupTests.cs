using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Quartz;
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
public sealed class IntervalJobSetupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddIntervalJob_ShouldRegisterTheJobUnderItsTypeName_WithoutOverlappingRuns()
    {
        await using var provider = Build(quartz => quartz.AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(5)));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var job = await scheduler.GetJobDetail(new JobKey(nameof(NoopJob)));

        job.ShouldNotBeNull();
        job.JobType.ShouldBe(typeof(NoopJob));
        job.ConcurrentExecutionDisallowed.ShouldBeTrue();
    }

    [Fact]
    public async Task AddIntervalJob_ShouldRepeatEveryInterval_StartingAfterTheGivenDelay_OnTheContainersClock()
    {
        await using var provider = Build(quartz => quartz.AddIntervalJob<NoopJob>(
            TimeSpan.FromHours(6),
            startAfter: TimeSpan.FromMinutes(2)));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var trigger = (await scheduler.GetTriggersOfJob(IntervalJobSetup.JobKeyFor<NoopJob>())).ShouldHaveSingleItem();

        var simple = trigger.ShouldBeAssignableTo<ISimpleTrigger>().ShouldNotBeNull();
        simple.RepeatInterval.ShouldBe(TimeSpan.FromHours(6));
        simple.RepeatCount.ShouldBe(-1);
        trigger.StartTimeUtc.ShouldBe(Now.AddMinutes(2));
    }

    [Fact]
    public async Task AddIntervalJob_ShouldStartAfterThirtySeconds_ByDefault()
    {
        await using var provider = Build(quartz => quartz.AddIntervalJob<NoopJob>(TimeSpan.FromMinutes(1)));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var trigger = (await scheduler.GetTriggersOfJob(IntervalJobSetup.JobKeyFor<NoopJob>())).ShouldHaveSingleItem();

        trigger.StartTimeUtc.ShouldBe(Now.AddSeconds(30));
    }

    [Fact]
    public async Task AddIntervalCommandJob_ShouldSendTheCommand_WithASendCommandJobKeyedByTheCommand()
    {
        await using var provider = Build(quartz => quartz.AddIntervalCommandJob<PingCommand>(TimeSpan.FromMinutes(1)));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var job = await scheduler.GetJobDetail(new JobKey("SendCommandJob-PingCommand"));

        job.ShouldNotBeNull().JobType.ShouldBe(typeof(SendCommandJob<PingCommand>));
    }

    [Fact]
    public void JobKeyFor_ShouldNameAGenericJobAfterItsTypeArguments()
    {
        IntervalJobSetup.JobKeyFor<NoopJob>().Name.ShouldBe("NoopJob");
        IntervalJobSetup.JobKeyFor<GenericJob<PingCommand>>().Name.ShouldBe("GenericJob-PingCommand");
        IntervalJobSetup.JobKeyFor<GenericJob<GenericJob<PingCommand>>>().Name.ShouldBe("GenericJob-GenericJob-PingCommand");
    }

    [Fact]
    public void AddIntervalJob_ShouldRefuseAnIntervalOrStartThatMakesNoSense()
    {
        var services = new ServiceCollection();

        services.AddQuartz(quartz =>
        {
            Should.Throw<ArgumentOutOfRangeException>(() => quartz.AddIntervalJob<NoopJob>(TimeSpan.Zero));
            Should.Throw<ArgumentOutOfRangeException>(() => quartz.AddIntervalJob<NoopJob>(
                TimeSpan.FromMinutes(1),
                startAfter: TimeSpan.FromSeconds(-1)));
        });
    }

    private static ServiceProvider Build(Action<IQuartzBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddQuartz(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddIntervalCommandJob_ShouldThrowAtRegistration_ForACommandReturningAValue()
        => Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddQuartz(quartz =>
                quartz.AddIntervalCommandJob<CountingCommand>(TimeSpan.FromMinutes(1))))
            .Message.ShouldContain("AddIntervalCommandJob<CountingCommand, Result<Int32>>");

    [Fact]
    public void AddIntervalCommandJob_ShouldRegister_ACommandReturningAValue_WithItsResultType()
        => Should.NotThrow(() => new ServiceCollection().AddQuartz(quartz =>
            quartz.AddIntervalCommandJob<CountingCommand, Result<int>>(TimeSpan.FromMinutes(1))));

    private sealed class CountingCommand : Resrcify.SharedKernel.Abstractions.Mediator.ICommand<int>;
}
