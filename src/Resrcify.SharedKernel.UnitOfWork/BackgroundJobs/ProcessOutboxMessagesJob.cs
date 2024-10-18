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
public sealed class ProcessOutboxMessagesJob<TDbContext>
    : IJob
    where TDbContext : DbContext
{
    private readonly TDbContext _context;
    private readonly IPublisher _publisher;
    private readonly int _processBatchSize;

    public ProcessOutboxMessagesJob(
        TDbContext context,
        IPublisher publisher,
        int processBatchSize = 20)
    {
        _context = context;
        _publisher = publisher;
        _processBatchSize = processBatchSize;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        List<OutboxMessage> messages = await _context
            .Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(x => x.OccurredOnUtc)
            .Take(_processBatchSize)
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