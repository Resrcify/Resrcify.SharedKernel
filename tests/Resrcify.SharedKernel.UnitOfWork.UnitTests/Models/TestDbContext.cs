using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

internal sealed class TestDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>().HasKey(x => x.Id);
        modelBuilder.Entity<Person>().Property(x => x.Name).HasMaxLength(10);
        modelBuilder.Entity<Person>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));

        modelBuilder.Entity<Person>().Property(x => x.IsDeleted).IsRequired();
        modelBuilder.Entity<Person>().Property(x => x.DeletedOnUtc);

        modelBuilder.Entity<Person>().Property(x => x.CreatedOnUtc);
        modelBuilder.Entity<Person>().Property(x => x.ModifiedOnUtc);

        modelBuilder.Entity<Person>().HasMany(x => x.Children).WithOne();

        modelBuilder.Entity<Child>().HasKey(x => x.Id);
        modelBuilder.Entity<Child>().Property(x => x.Name).HasMaxLength(10);
        modelBuilder.Entity<Child>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));

    }

    internal DbSet<Person> Persons { get; set; } = default!;
    internal DbSet<OutboxMessage> OutboxMessages { get; set; } = default!;
}
