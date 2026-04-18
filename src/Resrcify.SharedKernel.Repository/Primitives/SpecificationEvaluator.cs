using System.Linq;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.Repository;

namespace Resrcify.SharedKernel.Repository.Primitives;

public static class SpecificationEvaluator
{
    public static IQueryable<TEntity> GetQuery<TEntity>(
        IQueryable<TEntity> inputQueryable,
        ISpecification<TEntity> specification)
        where TEntity : class, IAggregateRoot
    {
        IQueryable<TEntity> queryable = inputQueryable;

        if (specification.Criteria is not null)
            queryable = queryable.Where(specification.Criteria);

        queryable = specification.IncludeExpressions.Aggregate(
            queryable,
            (current, includeExpressions) =>
                current.Include(includeExpressions));

        if (specification.OrderByExpression is not null)
            queryable = queryable.OrderBy(specification.OrderByExpression);
        else if (specification.OrderByDescendingExpression is not null)
            queryable = queryable.OrderByDescending(specification.OrderByDescendingExpression);

        if (specification.IsSplitQuery)
            queryable = queryable.AsSplitQuery();

        if (specification.IsNoTrackingQuery)
            queryable = queryable.AsNoTracking();

        return queryable;
    }
}