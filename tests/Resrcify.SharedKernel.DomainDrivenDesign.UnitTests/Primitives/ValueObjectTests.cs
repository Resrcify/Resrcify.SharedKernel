using System.Collections.Generic;
using FluentAssertions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

public class ValueObjectTests
{
    private class Address : ValueObject
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
        address1.Equals(address2).Should().BeTrue();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("124 Elm St", "Somewhere", "12345");

        // Act & Assert
        address1.Equals(address2).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_ShouldBeEqual_ForIdenticalValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("123 Elm St", "Somewhere", "12345");

        // Act & Assert
        address1.GetHashCode().Should().Be(address2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_ShouldDiffer_ForDifferentValueObjects()
    {
        // Arrange
        var address1 = new Address("123 Elm St", "Somewhere", "12345");
        var address2 = new Address("124 Elm St", "Somewhere", "12345");

        // Act & Assert
        address1.GetHashCode().Should().NotBe(address2.GetHashCode());
    }
}