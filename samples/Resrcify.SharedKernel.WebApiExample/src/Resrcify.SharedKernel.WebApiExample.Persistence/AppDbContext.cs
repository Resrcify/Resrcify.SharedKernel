using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.WebApiExample.Persistence;

public partial class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}