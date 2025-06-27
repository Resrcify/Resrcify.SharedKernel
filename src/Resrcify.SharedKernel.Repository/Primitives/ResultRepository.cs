using System.Threading;
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Repository.Abstractions;

namespace Resrcify.SharedKernel.Repository.Primitives;

public abstract class ResultRepository<TDbContext, TEntity, TId>(TDbContext context)
    : Repository<TDbContext, TEntity, TId>(context),
    IResultFetchRepository<TEntity, TId>
    where TDbContext : DbContext
    where TEntity : AggregateRoot<TId>
    where TId : notnull
{
    public new virtual async Task<Result<TEntity>> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken = default)
        => Result
            .Create(await base.GetByIdAsync(id, cancellationToken))
            .Match(
                entity => entity,
                new Error(
                    $"{typeof(TEntity).Name}.NotFound",
                    $"{typeof(TEntity).Name} with Id '{id}' was not found.",
                    ErrorType.NotFound));

    public new virtual async Task<Result<TEntity>> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Result
            .Create(await base.FirstOrDefaultAsync(predicate, cancellationToken))
            .Match(
                entity => entity,
                new Error(
                    $"{typeof(TEntity).Name}.NotFound",
                    $"{typeof(TEntity).Name} matching the specified criteria was not found.",
                    ErrorType.NotFound));
    public new async Task<Result<TEntity>> FirstOrDefaultAsync(
        Specification<TEntity, TId> specification,
        CancellationToken cancellationToken = default)
        => Result
            .Create(await base.FirstOrDefaultAsync(
                specification,
                cancellationToken))
            .Match(
                entity => entity,
                new Error(
                    $"{typeof(TEntity).Name}.NotFound",
                    $"{typeof(TEntity).Name} matching the specified criteria was not found.",
                    ErrorType.NotFound));
}