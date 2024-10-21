using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

public partial class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = default!;
    public DbSet<Company> Companies { get; set; } = default!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}