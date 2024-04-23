using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.GenericUnitOfWork.Interceptors;
using Resrcify.SharedKernel.GenericUnitOfWork.Outbox;

namespace Resrcify.SharedKernel.GenericUnitOfWork.UnitTests.Models;

public class TestDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Person>().HasKey(x => x.Id);
        builder.Entity<Person>().Property(x => x.Name).HasMaxLength(10);
        builder.Entity<Person>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));

        builder.Entity<Person>().Property(x => x.IsDeleted).IsRequired();
        builder.Entity<Person>().Property(x => x.DeletedOnUtc);

        builder.Entity<Person>().Property(x => x.CreatedOnUtc);
        builder.Entity<Person>().Property(x => x.ModifiedOnUtc);

        builder.Entity<Person>().HasMany(x => x.Children).WithOne();

        builder.Entity<Child>().HasKey(x => x.Id);
        builder.Entity<Child>().Property(x => x.Name).HasMaxLength(10);
        builder.Entity<Child>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));

    }

    public virtual DbSet<Person> Persons { get; set; } = default!;
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; } = default!;
}
