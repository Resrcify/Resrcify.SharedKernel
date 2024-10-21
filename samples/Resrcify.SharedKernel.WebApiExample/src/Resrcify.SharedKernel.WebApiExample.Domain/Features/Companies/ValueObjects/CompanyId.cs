using System;
using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

public sealed class CompanyId : ValueObject
{
    public Guid Value { get; }
    private CompanyId(Guid value)
        => Value = value;

    public static Result<CompanyId> Create(Guid value)
        => Result
            .Ensure(
                value,
                value => !value.Equals(Guid.Empty),
                DomainErrors.CompanyId.Empty)
            .Map(value => new CompanyId(value));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}
