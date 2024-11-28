using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Quartz;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using MediatR;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

[DisallowConcurrentExecution]
public sealed class ProcessOutboxMessagesNewtonsoftJob<TDbContext>
    : IJob
    where TDbContext : DbContext
{
    private readonly TDbContext _context;
    private readonly IPublisher _publisher;

    public ProcessOutboxMessagesNewtonsoftJob(
        TDbContext context,
        IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        if (!context.MergedJobDataMap.TryGetInt("ProcessBatchSize", out var batchSize))
            batchSize = 20;

        List<OutboxMessage> messages = await _context
            .Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(x => x.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(context.CancellationToken);

        foreach (OutboxMessage outboxMessage in messages)
        {
            IDomainEvent? domainEvent = JsonConvert
                .DeserializeObject<IDomainEvent>(
                    outboxMessage.Content,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All
                    });

            if (domainEvent is null)
                continue;

            await _publisher.Publish(domainEvent, context.CancellationToken);

            outboxMessage.ProcessedOnUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }
}