using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

public sealed class OrganizationNumber : ValueObject
{
    private const int Length = 10;
    public long Value { get; }
    private OrganizationNumber(long value)
        => Value = value;
    public static Result<OrganizationNumber> Create(string value)
        => Result
            .Ensure(
                value,
                (value => !string.IsNullOrEmpty(value), DomainErrors.OrganizationNumber.Empty),
                (value => value.ToString().Length == Length, DomainErrors.OrganizationNumber.InvalidLength),
                (value => value.Length > 0 && IsAllowedStartingDigit(value[0]), DomainErrors.OrganizationNumber.InvalidStartingDigit),
                (IsValidChecksum, DomainErrors.OrganizationNumber.InvalidChecksum))
            .Map(value => new OrganizationNumber(long.Parse(value)));
    private static bool IsAllowedStartingDigit(char firstDigit)
        => firstDigit is '1' ||
            firstDigit is '2' ||
            firstDigit is '3' ||
            firstDigit is '5' ||
            firstDigit is '7' ||
            firstDigit is '8' ||
            firstDigit is '9';
    private static bool IsValidChecksum(string number)
    {
        int sum = 0;
        bool isEvenDigitPlacement = false;

        for (int i = number.Length - 1; i >= 0; i--)
        {
            int n = int.Parse(number[i].ToString());

            if (isEvenDigitPlacement)
            {
                n *= 2;
                if (n > 9)
                    n -= 9;
            }

            sum += n;
            isEvenDigitPlacement = !isEvenDigitPlacement;
        }

        return sum % 10 == 0;
    }
    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}
