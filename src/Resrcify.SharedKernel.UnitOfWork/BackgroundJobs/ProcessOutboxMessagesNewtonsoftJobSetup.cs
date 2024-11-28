using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

public sealed class ProcessOutboxMessagesNewtonsoftJobSetup<TDbContext>(
    int processBatchSize = 20,
    int processIntervalInSeconds = 60,
    int delayInSecondsBeforeStart = 60)
    : IConfigureOptions<QuartzOptions>
    where TDbContext : DbContext
{
    public void Configure(QuartzOptions options)
    {
        var jobKey = new JobKey(nameof(ProcessOutboxMessagesNewtonsoftJob<TDbContext>));

        options
            .AddJob<ProcessOutboxMessagesNewtonsoftJob<TDbContext>>(jobBuilder =>
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