using System;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Models;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutboxMessageConfiguration();
        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(x => x.Id);
            order.HasPostgresRowVersion();
        });
        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.HasKey(x => x.Id);
            invoice.HasPostgresRowVersion(x => x.Version);
        });
    }
}

/// <summary>A second context, for what is kept per context.</summary>
internal sealed class OtherDbContext(DbContextOptions<OtherDbContext> options)
    : DbContext(options);

internal sealed class Order
{
    public Guid Id { get; set; }
}

internal sealed class Invoice
{
    public Guid Id { get; set; }

    public uint Version { get; set; }
}
