using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>A requesting service's DbContext: its own aggregates and the outbox. Nothing for the bus.</summary>
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options)
    : DbContext(options)
{
    public DbSet<PingRun> PingRuns => Set<PingRun>();
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PingRun>().HasKey(x => x.Id);
        modelBuilder.Entity<Note>().HasKey(x => x.Id);
        modelBuilder.ApplyOutboxMessageConfiguration(
            configureUnprocessedIndex: PostgresOutboxIndexes.Instance.PartialUnprocessed,
            configureDedupIndex: PostgresOutboxIndexes.Instance.PartialUniqueDedup);
    }
}
