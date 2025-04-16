using Microsoft.EntityFrameworkCore;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

internal sealed class TestDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>().HasKey(x => x.Id);
        modelBuilder.Entity<Person>().Property(x => x.Name).HasMaxLength(10);
        modelBuilder.Entity<Person>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));
        modelBuilder.Entity<Person>().HasMany(x => x.Children).WithOne().HasForeignKey(x => x.PersonId);
        modelBuilder.Entity<Child>().HasKey(x => x.Id);
        modelBuilder.Entity<Child>().Property(x => x.Name).HasMaxLength(10);
        modelBuilder.Entity<Child>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));
        modelBuilder.Entity<Child>().Property(x => x.PersonId).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));
    }

    public DbSet<Person> Persons { get; set; } = default!;

}
