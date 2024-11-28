using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

public sealed class ProcessOutboxMessagesJobSetup<TDbContext>(
    int processBatchSize = 20,
    int processIntervalInSeconds = 60,
    int delayInSecondsBeforeStart = 60)
    : IConfigureOptions<QuartzOptions>
    where TDbContext : DbContext
{
    public void Configure(QuartzOptions options)
    {
        var jobKey = new JobKey(nameof(ProcessOutboxMessagesJob<TDbContext>));

        options
            .AddJob<ProcessOutboxMessagesJob<TDbContext>>(jobBuilder =>
                jobBuilder
                    .WithIdentity(jobKey)
                    .UsingJobData("ProcessBatchSize", processBatchSize))
            .AddTrigger(
                trigger =>
                    trigger.ForJob(jobKey)
                        .StartAt(DateTime.UtcNow.AddSeconds(delayInSecondsBeforeStart))
                        .WithSimpleSchedule(
                            schedule =>
                                schedule.WithIntervalInSeconds(processIntervalInSeconds)
                                    .RepeatForever()));
    }
}