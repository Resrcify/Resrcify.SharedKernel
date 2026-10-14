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
public sealed class CronJobSetupTests
{
    private const string Every15Minutes = "0 0/15 * * * ?";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 7, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddCronJob_ShouldRegisterTheJobUnderItsTypeName_WithoutOverlappingRuns()
    {
        await using var provider = Build(quartz => quartz.AddCronJob<NoopJob>(Every15Minutes));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var job = await scheduler.GetJobDetail(IntervalJobSetup.JobKeyFor<NoopJob>());

        job.ShouldNotBeNull();
        job.Key.Name.ShouldBe(nameof(NoopJob));
        job.JobType.ShouldBe(typeof(NoopJob));
        job.ConcurrentExecutionDisallowed.ShouldBeTrue();
    }

    [Fact]
    public async Task AddCronJob_ShouldFireOnTheExpression_InUtc_FromTheContainersClock()
    {
        await using var provider = Build(quartz => quartz.AddCronJob<NoopJob>(Every15Minutes));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var trigger = (await scheduler.GetTriggersOfJob(IntervalJobSetup.JobKeyFor<NoopJob>())).ShouldHaveSingleItem();

        var cron = trigger.ShouldBeAssignableTo<ICronTrigger>().ShouldNotBeNull();
        cron.CronExpressionString.ShouldBe(Every15Minutes);
        cron.TimeZone.ShouldBe(TimeZoneInfo.Utc);
        trigger.StartTimeUtc.ShouldBe(Now);
        trigger.GetFireTimeAfter(Now).ShouldBe(new DateTimeOffset(2026, 10, 6, 12, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task AddCronJob_ShouldReadTheExpressionInTheGivenTimeZone()
    {
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
        await using var provider = Build(quartz => quartz.AddCronJob<NoopJob>("0 0 16 * * ?", plusTwo));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var trigger = (await scheduler.GetTriggersOfJob(IntervalJobSetup.JobKeyFor<NoopJob>())).ShouldHaveSingleItem();

        // 16:00 at +02:00 is 14:00 UTC.
        trigger.GetFireTimeAfter(Now).ShouldBe(new DateTimeOffset(2026, 10, 6, 14, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("every 15 minutes")]
    [InlineData("*/15 * * * *")]   // Unix cron: Quartz's has seconds first
    public void AddCronJob_ShouldRefuseAnExpressionThatDoesntParse_AtRegistration(string expression)
        => Should.Throw<ArgumentException>(() => new ServiceCollection().AddQuartz(quartz => quartz.AddCronJob<NoopJob>(expression)))
            .Message.ShouldContain("isn't a Quartz cron expression");

    [Fact]
    public void AddCronJob_ShouldRefuseAnExpressionThatNeverFires_AtRegistration()
        => Should.Throw<ArgumentException>(() => new ServiceCollection().AddQuartz(quartz => quartz.AddCronJob<NoopJob>("0 0 0 30 2 ?")))
            .Message.ShouldContain("never fires");

    [Fact]
    public async Task AddCronCommandJob_ShouldSendTheCommand_WithASendCommandJobKeyedByTheCommand()
    {
        await using var provider = Build(quartz => quartz.AddCronCommandJob<PingCommand>(Every15Minutes));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var job = await scheduler.GetJobDetail(new JobKey("SendCommandJob-PingCommand"));

        job.ShouldNotBeNull().JobType.ShouldBe(typeof(SendCommandJob<PingCommand>));
        (await scheduler.GetTriggersOfJob(job.Key)).ShouldHaveSingleItem().ShouldBeAssignableTo<ICronTrigger>();
    }

    [Fact]
    public void AddCronCommandJob_ShouldThrowAtRegistration_ForACommandReturningAValue()
        => Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddQuartz(quartz =>
                quartz.AddCronCommandJob<CountingCommand>(Every15Minutes)))
            .Message.ShouldContain("CountingCommand returns Result<Int32>");

    [Fact]
    public async Task AddCronCommandJob_ShouldRegister_ACommandReturningAValue_WithItsResultType()
    {
        await using var provider = Build(quartz => quartz.AddCronCommandJob<CountingCommand, Result<int>>(Every15Minutes));
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var job = await scheduler.GetJobDetail(new JobKey("SendCommandJob-CountingCommand-Result-Int32"));

        job.ShouldNotBeNull().JobType.ShouldBe(typeof(SendCommandJob<CountingCommand, Result<int>>));
    }

    private static ServiceProvider Build(Action<IQuartzBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddQuartz(configure);
        return services.BuildServiceProvider();
    }

    private sealed class CountingCommand : Resrcify.SharedKernel.Abstractions.Mediator.ICommand<int>;
}
