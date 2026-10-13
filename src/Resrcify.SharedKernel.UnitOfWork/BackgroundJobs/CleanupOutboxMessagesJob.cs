using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Deletes processed outbox messages older than the retention (default 7 days), so the outbox table and its indexes
/// don't grow forever. Messages not processed yet, and messages that gave up (marked with
/// <see cref="OutboxMessage.GivenUpProcessedOnUtc"/>, which is never before the cutoff), are kept.
/// </summary>
[DisallowConcurrentExecution]
public sealed partial class CleanupOutboxMessagesJob<TDbContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<CleanupOutboxMessagesJob<TDbContext>> logger,
    TimeProvider? timeProvider = null)
    : IJob
    where TDbContext : DbContext
{
    internal const string RetentionInDaysKey = "ProcessedRetentionInDays";
    internal const int DefaultRetentionInDays = 7;

    /// <summary>Rows deleted per statement, so a large backlog is deleted in short transactions.</summary>
    public const int DeleteBatchSize = 5_000;

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var retentionInDays = context.MergedJobDataMap.TryGetValue(RetentionInDaysKey, out var value)
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : DefaultRetentionInDays;
        var deleted = await DeleteProcessedAsync(
            scopeFactory,
            TimeSpan.FromDays(retentionInDays),
            timeProvider ?? TimeProvider.System,
            cancellationToken);
        if (deleted > 0)
            LogDeleted(deleted, retentionInDays);
    }

    /// <summary>Deletes processed messages older than <paramref name="retention"/>, in batches; returns how many.</summary>
    internal static async Task<int> DeleteProcessedAsync(
        IServiceScopeFactory scopeFactory,
        TimeSpan retention,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - retention;
        var total = 0;
        int deleted;
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
            deleted = await dbContext
                .Set<OutboxMessage>()
                .Where(message => message.ProcessedOnUtc != null && message.ProcessedOnUtc < cutoff)
                .OrderBy(message => message.ProcessedOnUtc)
                .Take(DeleteBatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
        }
        while (deleted == DeleteBatchSize);
        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} processed outbox messages older than {Days} days")]
    private partial void LogDeleted(int count, int days);
}
