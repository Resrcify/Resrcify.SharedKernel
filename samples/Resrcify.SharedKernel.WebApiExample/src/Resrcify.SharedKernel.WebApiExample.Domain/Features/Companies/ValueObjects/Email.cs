using System.Collections.Generic;
using System.Text.RegularExpressions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

public sealed class Email : ValueObject
{
    public const int MaxLength = 256;
    public const int MinLength = 6;
    private static readonly Regex _validEmail = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    private Email(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<Email> Create(string value)
        => Result
            .Ensure(
                value,
                (value => !string.IsNullOrEmpty(value), DomainErrors.Email.Empty),
                (value => value.Length >= MinLength, DomainErrors.Email.TooShort(value, MinLength)),
                (value => value.Length <= MaxLength, DomainErrors.Email.TooLong(value, MaxLength)),
                (_validEmail.IsMatch, DomainErrors.Email.Invalid))
            .Map(e => new Email(e));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}