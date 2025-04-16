using System;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Events;
using System.Linq;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Shouldly;

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
            .ShouldBeTrue();
        result.Value
            .ShouldNotBeNull();
        result.Value.Name.Value
            .ShouldBe(_validName);
        result.Value.OrganizationNumber.Value
            .ShouldBe(long.Parse(_validOrganizationNumber));
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
            .ShouldBeTrue();
        company.Name.Value
            .ShouldBe(_updatedName);
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
            .ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem();

        // Assert that the error is equal to DomainErrors.Name.Identical(_validName)
        result.Errors[0].ShouldBe(DomainErrors.Name.Identical(_validName));
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
            .ShouldBeEmpty();
        result.IsSuccess
            .ShouldBeTrue();

        company.Contacts.ShouldHaveSingleItem();
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
            .ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].ShouldBe(DomainErrors.Contact.EmailAlreadyExist(_validContactEmail));
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
            .ShouldBeTrue();
        company.Contacts
            .ShouldBeEmpty();
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
            .ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].ShouldBe(DomainErrors.Contact.NotFound(_validContactEmail));
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
            .ShouldBeTrue();
        result.Errors
            .ShouldBeEmpty();
        var updatedContact = company.Contacts[0];
        updatedContact.FirstName
            .ShouldBe(newFirstName);
        updatedContact.LastName
            .ShouldBe(newLastName);
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
            .ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem();
        result.Errors[0].ShouldBe(DomainErrors.Contact.NotFound(_validContactEmail));
    }

    [Fact]
    public void Create_ShouldRaiseCompanyCreatedEvent_WithCorrectValues()
    {
        // Act
        var result = Company.Create(_validCompanyId, _validName, _validOrganizationNumber);

        // Assert
        result.IsSuccess
            .ShouldBeTrue();
        var company = result.Value;

        company.GetDomainEvents().ShouldHaveSingleItem();
        var domainEvent = company.GetDomainEvents()
                    .FirstOrDefault(e => e is CompanyCreatedEvent) as CompanyCreatedEvent;

        domainEvent
            .ShouldBeOfType<CompanyCreatedEvent>();

        domainEvent
            .ShouldNotBeNull();
        domainEvent.CompanyId
            .ShouldBe(company.Id.Value);
        domainEvent.Id
            .ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void UpdateName_ShouldRaiseCompanyNameUpdatedEvent_WithCorrectValues()
    {
        // Arrange
        var company = Company.Create(_validCompanyId, _validName, _validOrganizationNumber);
        company.Errors.ShouldBeEmpty();

        // Act
        var result = company.Value.UpdateName(_updatedName);

        // Assert
        result.IsSuccess
            .ShouldBeTrue();

        var domainEvents = company.Value.GetDomainEvents();
        domainEvents.ShouldContain(e => e is CompanyNameUpdatedEvent);
        var domainEvent = domainEvents.OfType<CompanyNameUpdatedEvent>().SingleOrDefault();

        domainEvent
            .ShouldNotBeNull();
        domainEvent?.CompanyId
            .ShouldBe(company.Value.Id.Value);
        domainEvent?.OldName
            .ShouldBe(_validName);
        domainEvent?.NewName
            .ShouldBe(_updatedName);
        domainEvent?.Id
            .ShouldNotBe(Guid.Empty);
    }
}