using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

/// <summary>A context with one real migration, for applying migrations at start-up.</summary>
internal sealed class MigratedDbContext(DbContextOptions<MigratedDbContext> options)
    : DbContext(options)
{
    internal DbSet<Ledger> Ledgers => Set<Ledger>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Ledger>(ledger =>
        {
            ledger.ToTable("Ledgers");
            ledger.HasKey(x => x.Id);
        });
}

internal sealed class Ledger
{
    public Guid Id { get; set; }
}

/// <summary>
/// Creates <c>Ledgers</c>, slowly (a second's sleep), so two instances migrating at once overlap: without a migration
/// lock both would create the table and one would fail.
/// </summary>
[DbContext(typeof(MigratedDbContext))]
[Migration("20261006000000_CreateLedgers")]
internal sealed class CreateLedgers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("SELECT pg_sleep(1);");
        migrationBuilder.CreateTable(
            name: "Ledgers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Ledgers", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "Ledgers");
}
