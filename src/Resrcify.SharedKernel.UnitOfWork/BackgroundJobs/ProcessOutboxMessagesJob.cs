using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quartz;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using MediatR;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using System.Reflection;
using System.Text.Json;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

[DisallowConcurrentExecution]
public sealed class ProcessOutboxMessagesJob<TDbContext>
    : IJob
    where TDbContext : DbContext
{
    private readonly TDbContext _context;
    private readonly IPublisher _publisher;

    public ProcessOutboxMessagesJob(
        TDbContext context,
        IPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        if (!context.MergedJobDataMap.TryGetString("EventsAssemblyName", out string? assemblyName))
            assemblyName = Assembly.GetExecutingAssembly().FullName;

        if (!context.MergedJobDataMap.TryGetInt("ProcessBatchSize", out var batchSize))
            batchSize = 20;

        var assembly = Assembly.Load(assemblyName!);
        var messages = _context
            .Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(x => x.OccurredOnUtc)
            .Take(batchSize)
            .AsAsyncEnumerable();

        await foreach (OutboxMessage outboxMessage in messages.WithCancellation(context.CancellationToken))
        {
            Type? messageType = assembly.GetType(outboxMessage.Type);

            if (messageType is null)
                continue;

            IDomainEvent? domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(outboxMessage.Content, messageType);

            if (domainEvent is null)
                continue;

            await _publisher.Publish(domainEvent, context.CancellationToken);

            outboxMessage.ProcessedOnUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }
}