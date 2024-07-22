using Microsoft.EntityFrameworkCore;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

public class TestDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Person>().HasKey(x => x.Id);
        builder.Entity<Person>().Property(x => x.Name).HasMaxLength(10);
        builder.Entity<Person>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));
        builder.Entity<Person>().HasMany(x => x.Children).WithOne().HasForeignKey(x => x.PersonId);
        builder.Entity<Child>().HasKey(x => x.Id);
        builder.Entity<Child>().Property(x => x.Name).HasMaxLength(10);
        builder.Entity<Child>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));
        builder.Entity<Child>().Property(x => x.PersonId).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));
    }

    public virtual DbSet<Person> Persons { get; set; } = default!;

}
