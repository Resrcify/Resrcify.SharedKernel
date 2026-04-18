using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Resrcify.SharedKernel.Abstractions.Repository;

public interface ISpecification<TEntity>
{
    Expression<Func<TEntity, bool>>? Criteria { get; }
    bool IsSplitQuery { get; }
    bool IsNoTrackingQuery { get; }
    ICollection<Expression<Func<TEntity, object>>> IncludeExpressions { get; }
    Expression<Func<TEntity, object>>? OrderByExpression { get; }
    Expression<Func<TEntity, object>>? OrderByDescendingExpression { get; }
}