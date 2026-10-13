using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Extensions;

/// <summary><c>AddPostgresDbContext</c> with two contexts whose outboxes are set up differently.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class PostgresServiceCollectionExtensionsOutboxTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddPostgresDbContext_ShouldWriteEachContextsOutboxItsOwnWay_WhenTheirOptionsDiffer()
    {
        var services = new ServiceCollection();
        var configuration = TwoDatabases();
        services.AddLogging();
        services.AddPostgresDbContext<TestDbContext>(configuration, db => db
            .WithOutbox(outbox => outbox.OnConflictDoNothing = true)
            .RetryOnFailure());
        services.AddPostgresDbContext<ReportDbContext>(configuration, db => db
            .FromSection("ReportDatabase")
            .WithOutbox()
            .RetryOnFailure());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var shards = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
        await shards.Database.EnsureCreatedAsync();
        await reports.Database.EnsureCreatedAsync();
        try
        {
            // The report context's plain insert works under its retrying strategy: it no longer borrows the shard
            // context's ON CONFLICT insert, which opens a transaction of its own that the strategy refuses.
            var report = new Report(Guid.NewGuid());
            report.Publish();
            reports.Reports.Add(report);
            await reports.SaveChangesAsync();

            (await reports.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(1);

            // The shard context still inserts with ON CONFLICT (a direct save is refused under its strategy).
            var shard = new Shard(Guid.NewGuid(), "direct");
            shard.Rename("direct save");
            shards.Shards.Add(shard);
            await Should.ThrowAsync<InvalidOperationException>(() => shards.SaveChangesAsync());
        }
        finally
        {
            await shards.Database.EnsureDeletedAsync();
            await reports.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public void AddPostgresDbContext_ShouldThrow_WhenTwoContextsAskForDifferentSerializers()
    {
        var services = new ServiceCollection();
        var configuration = TwoDatabases();
        services.AddPostgresDbContext<TestDbContext>(configuration, db => db
            .WithOutbox(outbox => outbox.Serializer = new SystemTextJsonOutboxSerializer()));

        Should.Throw<InvalidOperationException>(() => services.AddPostgresDbContext<ReportDbContext>(configuration, db => db
                .FromSection("ReportDatabase")
                .WithOutbox(outbox => outbox.Serializer = new OtherSerializer())))
            .Message.ShouldContain("Give every context the same serializer");
    }

    private IConfiguration TwoDatabases()
    {
        var settings = postgres.IsolatedDatabaseSettings();
        foreach (var (key, value) in postgres.IsolatedDatabaseSettings().ToList())
            settings[key.Replace("Database:", "ReportDatabase:", StringComparison.Ordinal)] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private sealed class OtherSerializer : IOutboxSerializer
    {
        public string Serialize(IDomainEvent domainEvent)
            => throw new NotSupportedException();

        public IDomainEvent? Deserialize(string content)
            => throw new NotSupportedException();
    }

    private sealed class ReportDbContext(DbContextOptions<ReportDbContext> options)
        : DbContext(options)
    {
        internal DbSet<Report> Reports => Set<Report>();

        internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Report>(report =>
            {
                report.ToTable("Reports");
                report.HasKey(x => x.Id);
            });
            modelBuilder.ApplyOutboxMessageConfiguration();
        }
    }

    private sealed class Report(Guid id)
        : AggregateRoot<Guid>(id)
    {
        public void Publish()
            => RaiseDomainEvent(new ReportPublished(Guid.NewGuid(), Id));
    }

    private sealed record ReportPublished(Guid Id, Guid ReportId)
        : DomainEvent(Id);
}
