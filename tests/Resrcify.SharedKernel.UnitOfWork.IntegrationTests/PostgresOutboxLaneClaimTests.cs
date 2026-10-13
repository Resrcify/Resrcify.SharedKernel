using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

/// <summary>The PostgreSQL claim on an outbox mapped with other column names (a naming convention, HasColumnName).</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class PostgresOutboxLaneClaimTests(PostgresFixture pg)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TryClaimAsync_ShouldClaimAWaitingMessage_WhenTheColumnsAreRenamed()
    {
        var connectionString = pg.CreateIsolatedConnectionString();
        await using var context = SnakeCaseOutboxContext.Create(connectionString);
        await context.Database.EnsureCreatedAsync();
        var waiting = Message();
        var processed = Message();
        processed.ProcessedOnUtc = DateTime.UtcNow;
        context.AddRange(waiting, processed);
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();

        (await PostgresOutboxLaneClaim.Instance.TryClaimAsync(context, waiting.Id, default)).ShouldBeTrue();
        (await PostgresOutboxLaneClaim.Instance.TryClaimAsync(context, processed.Id, default)).ShouldBeFalse();
    }

    [Fact]
    public async Task TryClaimAsync_ShouldSkipAMessage_WhileAnotherTransactionHoldsIt()
    {
        var connectionString = pg.CreateIsolatedConnectionString();
        await using var holder = SnakeCaseOutboxContext.Create(connectionString);
        await holder.Database.EnsureCreatedAsync();
        var message = Message();
        holder.Add(message);
        await holder.SaveChangesAsync();
        await using var held = await holder.Database.BeginTransactionAsync();
        (await PostgresOutboxLaneClaim.Instance.TryClaimAsync(holder, message.Id, default)).ShouldBeTrue();

        await using var other = SnakeCaseOutboxContext.Create(connectionString);
        await using var otherTransaction = await other.Database.BeginTransactionAsync();

        (await PostgresOutboxLaneClaim.Instance.TryClaimAsync(other, message.Id, default)).ShouldBeFalse();
    }

    private static OutboxMessage Message()
        => new()
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = "Some.Event",
            Content = "{}",
        };

    private sealed class SnakeCaseOutboxContext(DbContextOptions<SnakeCaseOutboxContext> options)
        : DbContext(options)
    {
        public static SnakeCaseOutboxContext Create(string connectionString)
            => new(new DbContextOptionsBuilder<SnakeCaseOutboxContext>().UseNpgsql(connectionString).Options);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyOutboxMessageConfiguration(schema: "messaging");
            modelBuilder.Entity<OutboxMessage>(outbox =>
            {
                outbox.ToTable("outbox_messages", "messaging");
                outbox.Property(m => m.Id).HasColumnName("id");
                outbox.Property(m => m.ProcessedOnUtc).HasColumnName("processed_on_utc");
                outbox.Property(m => m.OccurredOnUtc).HasColumnName("occurred_on_utc");
            });
        }
    }
}
