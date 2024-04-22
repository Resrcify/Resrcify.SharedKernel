using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;

namespace Resrcify.SharedKernel.GenericRepository.UnitTests.GenericRepository;

public class TestDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Person>().HasKey(x => x.Id);
        builder.Entity<Person>().Property(x => x.Name).HasMaxLength(10);
        builder.Entity<Person>().Property(x => x.Id).HasConversion(x => x.Value, v => SocialSecurityNumber.Create(v));
    }

    public virtual DbSet<Person> Persons { get; set; } = default!;
}
