using System;
using System.Linq;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Shouldly;

namespace Resrcify.SharedKernel.WebApiExample.Domain.UnitTests.Companies;

public class ContactIdTests
{
    [Fact]
    public void Create_ShouldCreateValidContactId_WhenValidInputIsUsed()
    {
        // Arrange
        var validGuid = Guid.NewGuid();

        // Act
        var result = ContactId.Create(validGuid);

        // Assert
        result.IsSuccess
            .ShouldBeTrue();
        result.Value
            .ShouldNotBeNull();
        result.Value.Value
            .ShouldBe(validGuid);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsEmpty()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act
        var result = ContactId.Create(emptyGuid);

        // Assert
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].ShouldBe(DomainErrors.CompanyId.Empty);
    }

    [Fact]
    public void GetAtomicValues_ShouldReturnCorrectValues()
    {
        // Arrange
        var validGuid = Guid.NewGuid();
        var contactId = ContactId.Create(validGuid).Value;

        // Act
        var atomicValues = contactId.GetAtomicValues().ToArray();

        // Assert
        atomicValues.ShouldHaveSingleItem();
        atomicValues[0].ShouldBe(validGuid);
    }
}