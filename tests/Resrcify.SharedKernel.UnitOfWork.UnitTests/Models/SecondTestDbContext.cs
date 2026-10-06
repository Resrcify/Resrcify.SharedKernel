using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Extensions;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>A second DbContext with its own outbox, as a service with two databases has.</summary>
internal sealed class SecondTestDbContext(DbContextOptions<SecondTestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyOutboxMessageConfiguration();
}
