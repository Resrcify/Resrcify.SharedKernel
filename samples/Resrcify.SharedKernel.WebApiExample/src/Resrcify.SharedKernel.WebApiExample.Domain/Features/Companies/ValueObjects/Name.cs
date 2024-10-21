using System.Collections.Generic;
using System.Text.RegularExpressions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

public sealed class Name : ValueObject
{
    public const int MaxLength = 100;
    public const int MinLength = 1;
    private static readonly Regex _allowedCharacters = new(@"^[a-zA-ZåäöÅÄÖ0-9]+( [a-zA-ZåäöÅÄÖ0-9]+)*$");
    public string Value { get; }
    private Name(string value)
        => Value = value;
    public static Result<Name> Create(string value)
        => Result
            .Ensure(
                value,
                (value => !string.IsNullOrEmpty(value), DomainErrors.Name.Empty),
                (value => value.Length >= MinLength, DomainErrors.Name.TooShort(value, MinLength)),
                (value => value.Length <= MaxLength, DomainErrors.Name.TooLong(value, MaxLength)),
                (_allowedCharacters.IsMatch, DomainErrors.Name.Invalid))
            .Map(value => new Name(value));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}
