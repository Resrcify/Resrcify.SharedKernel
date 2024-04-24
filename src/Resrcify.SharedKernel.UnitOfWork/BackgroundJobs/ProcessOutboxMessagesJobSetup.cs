using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

public sealed class ProcessOutboxMessagesJobSetup<TDbContext> : IConfigureOptions<QuartzOptions>
    where TDbContext : DbContext
{
    public void Configure(QuartzOptions options)
    {
        var jobKey = new JobKey(nameof(ProcessOutboxMessagesJob<TDbContext>));

        options
            .AddJob<ProcessOutboxMessagesJob<TDbContext>>(jobBuilder => jobBuilder.WithIdentity(jobKey))
            .AddTrigger(
                trigger =>
                    trigger.ForJob(jobKey)
                        .StartAt(DateTime.UtcNow.AddMinutes(1))
                        .WithSimpleSchedule(
                            schedule =>
                                schedule.WithIntervalInSeconds(60)
                                    .RepeatForever()));
    }
}