using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options)
    : DbContext(options)
{
    internal DbSet<TestAggregate> Aggregates => Set<TestAggregate>();
    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestAggregate>(b =>
        {
            b.ToTable("Aggregates");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.Content).IsRequired();
        });
    }
}
