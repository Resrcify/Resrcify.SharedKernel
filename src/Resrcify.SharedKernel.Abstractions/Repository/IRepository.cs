using System.Threading;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.Abstractions.Repository;

/// <summary>
/// The aggregates of one type, like a collection: a fetch gives the aggregate, or <see langword="null"/> when there is
/// none. Whether "none" is an error is the caller's decision (a handler turns it into its own <c>NotFound</c> error,
/// e.g. with <c>ToResult</c>). A service's repository interface derives from this and adds the queries its handlers
/// need.
/// </summary>
public interface IRepository<TEntity, TId>
    where TEntity : class, IAggregateRoot<TId>
    where TId : notnull
{
    Task<TEntity?> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TEntity> GetAllAsync();
    IAsyncEnumerable<TEntity> FindAsync(
        Expression<Func<TEntity, bool>> predicate);
    IAsyncEnumerable<TEntity> FindAsync(
        ISpecification<TEntity> specification);

    Task<bool> ExistsAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default);

    void Remove(
        TEntity entity);
    void RemoveRange(
        IEnumerable<TEntity> entities);
}