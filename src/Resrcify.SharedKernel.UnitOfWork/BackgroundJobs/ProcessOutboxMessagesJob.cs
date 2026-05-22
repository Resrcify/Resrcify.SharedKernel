using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

[DisallowConcurrentExecution]
public sealed class ProcessOutboxMessagesJob<TDbContext>(IServiceScopeFactory scopeFactory)
    : IJob
    where TDbContext : DbContext
{
    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;

        if (!context.MergedJobDataMap.TryGetInt(
            "ProcessBatchSize",
            out var batchSize))
            batchSize = 20;
        if (!context.MergedJobDataMap.TryGetInt(
            "ProcessMaxRetryCount",
            out var maxRetryCount))
            maxRetryCount = 3;

        var messages = await ReadBatchAsync(
            batchSize,
            maxRetryCount,
            cancellationToken);

        foreach (var message in messages)
            await ProcessMessageAsync(message, cancellationToken);
    }

    // No-tracking projection — only the columns the job needs, so the covering index
    // can serve the read. Poison messages (RetryCount at the maximum) are skipped.
    private async Task<List<OutboxMessageToProcess>> ReadBatchAsync(
        int batchSize,
        int maxRetryCount,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        return await context
            .Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null && m.RetryCount < maxRetryCount)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(batchSize)
            .Select(m => new OutboxMessageToProcess(
                m.Id,
                m.Content,
                m.RetryCount))
            .ToListAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(
        OutboxMessageToProcess message,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;

        // A dedicated DI scope per message: the DbContext, the event handlers and the
        // "mark processed" update are isolated to this message and this transaction.
        // The job never touches a DbContext (or its change tracker) shared with other
        // work, so there is nothing it can clobber by mistake.
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
            var context = provider.GetRequiredService<TDbContext>();
            var publisher = provider.GetRequiredService<IPublisher>();
            var serializer = provider.GetRequiredService<IOutboxSerializer>();

            try
            {
                // Publishing the event and marking the message processed commit
                // together: the message is only marked done if everything its
                // handlers persisted commits as well.
                await unitOfWork.ExecuteInTransactionAsync(
                    async token =>
                    {
                        var domainEvent = serializer.Deserialize(message.Content)
                            ?? throw new InvalidOperationException(
                                "Outbox message content could not be deserialized.");

                        await publisher.Publish(domainEvent, token);

                        MarkProcessed(context, message.Id);
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failure = exception;
            }
        }

        if (failure is not null)
            await RecordFailureAsync(message, failure, cancellationToken);
    }

    // Marks the row processed through the change tracker (a stub entity, no load
    // required) so the UPDATE participates in the same transaction and SaveChanges
    // as the work the event handlers persisted.
    private static void MarkProcessed(TDbContext context, Guid id)
    {
        var stub = new OutboxMessage { Id = id };
        context.Attach(stub);
        stub.ProcessedOnUtc = DateTime.UtcNow;
    }

    // Records the failure in a fresh scope (the processing scope was disposed along
    // with its rolled-back transaction and the failed handler's tracked entities).
    // The row stays unprocessed and retries until RetryCount reaches the configured
    // maximum, after which the polling query skips it.
    private async Task RecordFailureAsync(
        OutboxMessageToProcess message,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        var context = provider.GetRequiredService<TDbContext>();

        var stub = new OutboxMessage { Id = message.Id };
        context.Attach(stub);
        stub.Error = exception.ToString();
        stub.RetryCount = message.RetryCount + 1;

        await unitOfWork.CompleteAsync(cancellationToken);
    }

    private sealed record OutboxMessageToProcess(
        Guid Id,
        string Content,
        int RetryCount);
}
