using System;
using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

public sealed class ContactId : ValueObject
{
    public Guid Value { get; }
    private ContactId(Guid value)
        => Value = value;

    public static Result<ContactId> Create(Guid value)
        => Result
            .Ensure(
                value,
                value => !value.Equals(Guid.Empty),
                DomainErrors.CompanyId.Empty)
            .Map(value => new ContactId(value));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}
