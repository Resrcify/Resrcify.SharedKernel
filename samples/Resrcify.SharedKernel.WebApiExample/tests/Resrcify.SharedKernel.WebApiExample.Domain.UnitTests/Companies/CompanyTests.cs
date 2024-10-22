using System;
using FluentAssertions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Events;
using System.Linq;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Domain.UnitTests.Companies;

public class CompanyTests
{
    private readonly Guid _validCompanyId = Guid.NewGuid();
    private readonly string _validName = "Test Company";
    private readonly string _validOrganizationNumber = "5555555555";
    private readonly string _validContactEmail = "contact@example.com";
    private readonly string _validFirstName = "John";
    private readonly string _validLastName = "Doe";
    private readonly string _updatedName = "New Company Name";

    [Fact]
    public void Create_ShouldCreateValidCompany_WhenValidInputIsUsed()
    {
        // Act
        var result = Company.Create(_validCompanyId, _validName, _validOrganizationNumber);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();
        result.Value
            .Should()
            .NotBeNull();
        result.Value.Name.Value
            .Should()
            .Be(_validName);
        result.Value.OrganizationNumber.Value
            .Should()
            .Be(long.Parse(_validOrganizationNumber));
    }

    [Fact]
    public void UpdateName_ShouldUpdateCompanyName_WhenValidNewNameIsProvided()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;

        // Act
        var result = company.UpdateName(_updatedName);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();
        company.Name.Value
            .Should()
            .Be(_updatedName);
    }

    [Fact]
    public void UpdateName_ShouldReturnFailureResult_WhenNewNameIsIdentical()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;

        // Act
        var result = company.UpdateName(_validName);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .ContainSingle()
                .Which
                    .Should()
                    .Be(DomainErrors.Name.Identical(_validName));
    }

    [Fact]
    public void AddContact_ShouldAddNewContact_WhenValidDetailsAreProvided()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;

        // Act
        var result = company.AddContact(_validFirstName, _validLastName, _validContactEmail);

        // Assert
        result.Errors
            .Should()
            .BeEmpty();
        result.IsSuccess
            .Should()
            .BeTrue();

        company.Contacts.Should().HaveCount(1);
    }

    [Fact]
    public void AddContact_ShouldReturnFailureResult_WhenEmailAlreadyExists()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;
        company.AddContact(_validFirstName, _validLastName, _validContactEmail);

        // Act
        var result = company.AddContact("Jane", "Smith", _validContactEmail);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .ContainSingle()
                .Which
                    .Should()
                    .Be(DomainErrors.Contact.EmailAlreadyExist(_validContactEmail));
    }

    [Fact]
    public void RemoveContactByEmail_ShouldRemoveContact_WhenValidEmailIsProvided()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;
        company.AddContact(_validFirstName, _validLastName, _validContactEmail);

        // Act
        var result = company.RemoveContactByEmail(_validContactEmail);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();
        company.Contacts
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void RemoveContactByEmail_ShouldReturnFailureResult_WhenEmailDoesNotExist()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;

        // Act
        var result = company.RemoveContactByEmail(_validContactEmail);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .ContainSingle()
                .Which
                .Should()
                .Be(DomainErrors.Contact.NotFound(_validContactEmail));
    }

    [Fact]
    public void UpdateContactByEmail_ShouldUpdateContact_WhenEmailExists()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;
        company.AddContact(_validFirstName, _validLastName, _validContactEmail);
        var newFirstName = Name.Create("Jane").Value;
        var newLastName = Name.Create("Doe").Value;
        // Act
        var result = company.UpdateContactByEmail(_validContactEmail, newFirstName.Value, newLastName.Value);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .BeEmpty();
        var updatedContact = company.Contacts[0];
        updatedContact.FirstName
            .Should()
            .Be(newFirstName);
        updatedContact.LastName
            .Should()
            .Be(newLastName);
    }

    [Fact]
    public void UpdateContactByEmail_ShouldReturnFailureResult_WhenEmailDoesNotExist()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber).Value;

        // Act
        var result = company.UpdateContactByEmail(_validContactEmail, "Jane", "Doe");

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .ContainSingle()
                .Which
                .Should()
                .Be(DomainErrors.Contact.NotFound(_validContactEmail));
    }

    [Fact]
    public void Create_ShouldRaiseCompanyCreatedEvent_WithCorrectValues()
    {
        // Act
        var result = Company.Create(_validCompanyId, _validName, _validOrganizationNumber);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();
        var company = result.Value;

        var domainEvent = company.GetDomainEvents()
            .Should()
            .ContainSingle()
                .Which
                    .Should()
                    .BeOfType<CompanyCreatedEvent>().Subject;

        domainEvent
            .Should()
            .NotBeNull();
        domainEvent.CompanyId
            .Should()
            .Be(company.Id.Value);
        domainEvent.Id
            .Should()
            .NotBeEmpty();
    }

    [Fact]
    public void UpdateName_ShouldRaiseCompanyNameUpdatedEvent_WithCorrectValues()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber);
        company.Errors.Should().BeEmpty();

        // Act
        var result = company.Value.UpdateName(_updatedName);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();

        var domainEvents = company.Value.GetDomainEvents();
        domainEvents.Should().Contain(e => e is CompanyNameUpdatedEvent);
        var domainEvent = domainEvents.OfType<CompanyNameUpdatedEvent>().SingleOrDefault();

        domainEvent
            .Should()
            .NotBeNull();
        domainEvent?.CompanyId
            .Should()
            .Be(company.Value.Id.Value);
        domainEvent?.OldName
            .Should()
            .Be(_validName);
        domainEvent?.NewName
            .Should()
            .Be(_updatedName);
        domainEvent?.Id
            .Should()
            .NotBeEmpty();
    }
}