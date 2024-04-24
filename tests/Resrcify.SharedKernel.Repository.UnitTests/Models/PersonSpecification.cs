using System;
using System.Linq.Expressions;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;
using Resrcify.SharedKernel.Repository.Primitives;

namespace Resrcify.SharedKernel.Repository.UnitTests.Models;

public class PersonSpecification : Specification<Person, SocialSecurityNumber>
{
    public PersonSpecification(
        Expression<Func<Person, bool>>? criteria = null,
        Expression<Func<Person, object>>? orderBy = null,
        Expression<Func<Person, object>>? orderByDecending = null,
        Expression<Func<Person, object>>? addInclude = null,
        bool isSplitQuery = false,
        bool isNoTrackingQuery = false
    ) : base(criteria)
    {
        if (orderBy is not null)
            AddOrderBy(orderBy);
        if (orderByDecending is not null)
            AddOrderByDescending(orderByDecending);
        if (addInclude is not null)
            AddInclude(addInclude);
        IsSplitQuery = isSplitQuery;
        IsNoTrackingQuery = isNoTrackingQuery;
    }
}