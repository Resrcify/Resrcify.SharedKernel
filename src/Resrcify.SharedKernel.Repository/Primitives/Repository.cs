using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.Repository.Abstractions;

namespace Resrcify.SharedKernel.Repository.Primitives;

public abstract class Repository<TDbContext, TEntity, TId>
    : IRepository<TEntity, TId>,
    INullableFetchRepository<TEntity, TId>
    where TDbContext : DbContext
    where TEntity : AggregateRoot<TId>
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
        Specification<TEntity, TId> specification,
        CancellationToken cancellationToken = default)
        => await ApplySpecification(specification)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);


    public virtual IAsyncEnumerable<TEntity> GetAllAsync()
        => Context
            .Set<TEntity>()
            .AsAsyncEnumerable();
    public virtual IAsyncEnumerable<TEntity> FindAsync(Expression<Func<TEntity, bool>> predicate)
        => Context
            .Set<TEntity>()
            .Where(predicate)
            .AsAsyncEnumerable();
    public IAsyncEnumerable<TEntity> FindAsync(Specification<TEntity, TId> specification)
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


    protected IQueryable<TEntity> ApplySpecification(Specification<TEntity, TId> specification)
        => SpecificationEvaluator
            .GetQuery(
                Context.Set<TEntity>(),
                specification);
}