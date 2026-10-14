using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
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
public sealed class FactoryCommandJobTests
{
    private const string Every15Minutes = "0 0/15 * * * ?";
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 7, 0, TimeSpan.Zero);

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly TaskCompletionSource<object> _sent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FactoryCommandJobTests()
    {
        _sender
            .Send<Result>(Arg.Any<StampCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Sent(call.Arg<IRequest<Result>>(), Result.Success()));
        _sender
            .Send<Result<int>>(Arg.Any<StampCountCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Sent(call.Arg<IRequest<Result<int>>>(), Result.Success(1)));
    }

    [Fact]
    public async Task AddIntervalCommandJob_WithAFactory_ShouldSendTheCommandItMakes_FromTheRunsScope()
    {
        var sent = await RunOnceAsync(
            quartz => quartz.AddIntervalCommandJob<StampCommand>(
                TimeSpan.FromMinutes(15),
                provider => new StampCommand(provider.GetRequiredService<PayoutClock>().UtcNow)),
            new JobKey("SendCommandJob-StampCommand"));

        sent.ShouldBe(new StampCommand(Now));
    }

    [Fact]
    public async Task AddIntervalCommandJob_WithAFactoryAndAResponse_ShouldSendTheCommandItMakes_FromTheRunsScope()
    {
        var sent = await RunOnceAsync(
            quartz => quartz.AddIntervalCommandJob<StampCountCommand, Result<int>>(
                TimeSpan.FromMinutes(15),
                provider => new StampCountCommand(provider.GetRequiredService<PayoutClock>().UtcNow)),
            new JobKey("SendCommandJob-StampCountCommand-Result-Int32"));

        sent.ShouldBe(new StampCountCommand(Now));
    }

    [Fact]
    public async Task AddCronCommandJob_WithAFactory_ShouldSendTheCommandItMakes_FromTheRunsScope()
    {
        var sent = await RunOnceAsync(
            quartz => quartz.AddCronCommandJob<StampCommand>(
                Every15Minutes,
                provider => new StampCommand(provider.GetRequiredService<PayoutClock>().UtcNow)),
            new JobKey("SendCommandJob-StampCommand"));

        sent.ShouldBe(new StampCommand(Now));
    }

    [Fact]
    public async Task AddCronCommandJob_WithAFactoryAndAResponse_ShouldSendTheCommandItMakes_FromTheRunsScope()
    {
        var sent = await RunOnceAsync(
            quartz => quartz.AddCronCommandJob<StampCountCommand, Result<int>>(
                Every15Minutes,
                provider => new StampCountCommand(provider.GetRequiredService<PayoutClock>().UtcNow)),
            new JobKey("SendCommandJob-StampCountCommand-Result-Int32"));

        sent.ShouldBe(new StampCountCommand(Now));
    }

    [Fact]
    public void FactoryCommandJob_ShouldRefuseACommandReturningAValue_SentAsAResult()
        => Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddQuartz(quartz =>
                quartz.AddIntervalCommandJob<StampCountCommand>(TimeSpan.FromMinutes(1), _ => new StampCountCommand())))
            .Message.ShouldContain("StampCountCommand returns Result<Int32>");

    // Builds a scheduler on a container that checks scopes (a scoped service resolved from the root throws), with a
    // scoped PayoutClock reading the registered TimeProvider, then runs the job once and returns what it sent.
    private async Task<object> RunOnceAsync(
        Action<IQuartzBuilder> register,
        JobKey jobKey)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddScoped<PayoutClock>();
        services.AddSingleton(_sender);
        services.AddQuartz(register);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        try
        {
            await scheduler.TriggerJob(jobKey);
            return await _sent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    private Task<TResponse> Sent<TResponse>(
        IRequest<TResponse> command,
        TResponse response)
    {
        _sent.TrySetResult(command);
        return Task.FromResult(response);
    }

    /// <summary>A scoped service a command factory reads, which only the run's scope can give.</summary>
    private sealed class PayoutClock(TimeProvider time)
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
