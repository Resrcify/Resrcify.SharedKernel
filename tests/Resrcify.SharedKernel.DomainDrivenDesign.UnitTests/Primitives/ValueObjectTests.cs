using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class ValueObjectTests
{
    private sealed class Address : ValueObject
    {
        public string Street { get; }
        public string City { get; }
        public string PostalCode { get; }

        public Address(string street, string city, string postalCode)
        {
            Street = street;
            City = city;
            PostalCode = postalCode;
        }

        public override IEnumerable<object> GetAtomicValues()
        {
            yield return Street;
            yield return City;
            yield return PostalCode;
        }
    }

    [Fact]
    public void Equals_ShouldReturnTrue_ForIdenticalValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("123 Elm St", "Somewhere", "12345");

        // Act & Assert
        address1.Equals(address2).ShouldBeTrue();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("124 Elm St", "Somewhere", "12345");

        // Act & Assert
        address1.Equals(address2).ShouldBeFalse();
    }

    [Fact]
    public void GetHashCode_ShouldBeEqual_ForIdenticalValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("123 Elm St", "Somewhere", "12345");

        // Act & Assert
        address1.GetHashCode().ShouldBe(address2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_ShouldDiffer_ForDifferentValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("124 Elm St", "Somewhere", "12345");

        // Act & Assert
        address1.GetHashCode().ShouldNotBe(address2.GetHashCode());
    }

    [Fact]
    public void Operator_Equals_ShouldReturnTrue_ForIdenticalValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("123 Elm St", "Somewhere", "12345");

        // Act & Assert
        (address1 == address2).ShouldBeTrue();
    }

    [Fact]
    public void Operator_Equals_ShouldReturnFalse_ForDifferentValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("124 Elm St", "Somewhere", "12345");

        // Act & Assert
        (address1 == address2).ShouldBeFalse();
    }

    [Fact]
    public void Operator_NotEquals_ShouldReturnTrue_ForDifferentValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("124 Elm St", "Somewhere", "12345");

        // Act & Assert
        (address1 != address2).ShouldBeTrue();
    }

    [Fact]
    public void Operator_NotEquals_ShouldReturnFalse_ForIdenticalValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("123 Elm St", "Somewhere", "12345");

        // Act & Assert
        (address1 != address2).ShouldBeFalse();
    }

    [Fact]
    public void EqualityOperators_ShouldTreatNullAsEqualOnlyToNull_WhenEitherSideIsNull()
    {
        // Through a call, so the analyzer can't assume what the operators return.
        var none = Opaque<Address>(null);
        var alsoNone = Opaque<Address>(null);
        var address = Opaque(new Address("Main", "City", "123"));

        (none == alsoNone).ShouldBeTrue();
        (none != null).ShouldBeFalse();
        (address == null).ShouldBeFalse();
        (address != null).ShouldBeTrue();
    }

    [Fact]
    public void Equals_ShouldBeFalse_WhenTwoDifferentValueObjectsHaveTheSameValues()
    {
        var email = new Email("a");
        var username = new Username("a");

        email.Equals(username).ShouldBeFalse();
        (email == username).ShouldBeFalse();
        new HashSet<ValueObject> { email, username }.Count.ShouldBe(2);
    }

    private static T? Opaque<T>(T? value)
        where T : class
        => value;

    private sealed class Email(string value) : ValueObject
    {
        public override IEnumerable<object> GetAtomicValues()
        {
            yield return value;
        }
    }

    private sealed class Username(string value) : ValueObject
    {
        public override IEnumerable<object> GetAtomicValues()
        {
            yield return value;
        }
    }
}
