using System;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Registers <see cref="ProcessOutboxMessagesJob{TDbContext}"/> on a Quartz scheduler.
/// </summary>
/// <remarks>
/// Quartz 4 removed the <c>QuartzOptions.AddJob</c> / <c>AddTrigger</c> extensions this used to call from an
/// <c>IConfigureOptions&lt;QuartzOptions&gt;</c>, so the registration now hangs off the builder that
/// <c>AddQuartz</c> hands out:
/// <code>
/// services.AddQuartz(quartz => quartz.AddProcessOutboxMessagesJob&lt;AppDbContext&gt;());
/// </code>
/// Both the job and the trigger are built when the scheduler is, not when this is called, so
/// <paramref name="delayInSecondsBeforeStart"/> still counts from scheduler start-up, as it did before.
/// </remarks>
public static class ProcessOutboxMessagesJobSetup
{
    public static IQuartzBuilder AddProcessOutboxMessagesJob<TDbContext>(
        this IQuartzBuilder quartz,
        int processBatchSize = ProcessOutboxMessagesJob<DbContext>.DefaultProcessBatchSize,
        int processIntervalInSeconds = 60,
        int delayInSecondsBeforeStart = 60,
        int processMaxRetryCount = ProcessOutboxMessagesJob<DbContext>.DefaultProcessMaxRetryCount)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(quartz);

        var jobKey = new JobKey(nameof(ProcessOutboxMessagesJob<TDbContext>));

        return quartz
            .AddJob<ProcessOutboxMessagesJob<TDbContext>>(job => job
                .WithIdentity(jobKey)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessBatchSizeKey,
                    processBatchSize)
                .UsingJobData(
                    ProcessOutboxMessagesJob<TDbContext>.ProcessMaxRetryCountKey,
                    processMaxRetryCount))
            .AddTrigger<ProcessOutboxMessagesJob<TDbContext>>(trigger => trigger
                .ForJob(jobKey)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(delayInSecondsBeforeStart))
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(TimeSpan.FromSeconds(processIntervalInSeconds))
                    .RepeatForever()));
    }
}
