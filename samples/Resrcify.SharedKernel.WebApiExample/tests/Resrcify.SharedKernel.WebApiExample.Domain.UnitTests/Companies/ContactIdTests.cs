using System;
using System.Linq;
using FluentAssertions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

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
            .Should()
            .BeTrue();
        result.Value
            .Should()
            .NotBeNull();
        result.Value.Value
            .Should()
            .Be(validGuid);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsEmpty()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act
        var result = ContactId.Create(emptyGuid);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .ContainSingle()
                .Which
                    .Should()
                    .Be(DomainErrors.CompanyId.Empty);
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
        atomicValues
            .Should()
            .ContainSingle()
                .Which
                    .Should()
                    .Be(validGuid);
    }
}