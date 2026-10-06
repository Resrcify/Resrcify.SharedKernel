using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.Repository;

namespace Resrcify.SharedKernel.Repository.Primitives;

/// <summary>
/// The EF Core implementation of <see cref="IRepository{TEntity, TId}"/>: a service's repository derives from it and
/// implements its own repository interface. Override a fetch to change how the aggregate is loaded (e.g. with its
/// children).
/// </summary>
public abstract class Repository<TDbContext, TEntity, TId>
    : IRepository<TEntity, TId>
    where TDbContext : DbContext
    where TEntity : class, IAggregateRoot<TId>
    where TId : notnull
{
    protected TDbContext Context { get; }
    protected Repository(TDbContext context)
        => Context = context;

    public virtual async Task<TEntity?> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken = default)
        => await Context
            .Set<TEntity>()
            .FindAsync([id], cancellationToken: cancellationToken);
    public virtual async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await Context
            .Set<TEntity>()
            .FirstOrDefaultAsync(predicate, cancellationToken: cancellationToken);
    public virtual async Task<TEntity?> FirstOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
        => await ApplySpecification(specification)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);


    public virtual IAsyncEnumerable<TEntity> GetAllAsync()
        => Context
            .Set<TEntity>()
            .AsAsyncEnumerable();
    public virtual IAsyncEnumerable<TEntity> FindAsync(
        Expression<Func<TEntity, bool>> predicate)
        => Context
            .Set<TEntity>()
            .Where(predicate)
            .AsAsyncEnumerable();
    public IAsyncEnumerable<TEntity> FindAsync(
        ISpecification<TEntity> specification)
        => ApplySpecification(specification)
            .AsAsyncEnumerable();
    public async Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
        => await Context
            .Set<TEntity>()
            .AddAsync(entity, cancellationToken);
    public async Task AddRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
        => await Context
            .Set<TEntity>()
            .AddRangeAsync(entities, cancellationToken);


    public async Task<bool> ExistsAsync(
        TId id,
        CancellationToken cancellationToken = default)
        => await Context
            .Set<TEntity>()
            .AnyAsync(
                entity => Equals(entity.Id, id),
                cancellationToken: cancellationToken);
    public async Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await Context
            .Set<TEntity>()
            .AnyAsync(predicate, cancellationToken: cancellationToken);


    public void Remove(TEntity entity)
        => Context
            .Set<TEntity>()
            .Remove(entity);
    public void RemoveRange(IEnumerable<TEntity> entities)
        => Context
            .Set<TEntity>()
            .RemoveRange(entities);


    protected IQueryable<TEntity> ApplySpecification(
        ISpecification<TEntity> specification)
        => SpecificationEvaluator
            .GetQuery(
                Context.Set<TEntity>(),
                specification);
}