using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

public class SocialSecurityNumber : ValueObject
{
    public int Value { get; private set; }
    private SocialSecurityNumber(int value)
    {
        Value = value;
    }
    public static SocialSecurityNumber Create(int value)
        => new(value);
    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}
