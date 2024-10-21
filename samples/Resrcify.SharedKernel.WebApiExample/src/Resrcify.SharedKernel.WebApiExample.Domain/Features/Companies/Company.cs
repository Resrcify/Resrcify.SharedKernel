using System;
using System.Collections.Generic;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Entities;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Enums;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Events;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;

public sealed class Company
    : AggregateRoot<CompanyId>,
        IAuditableEntity,
        IDeletableEntity
{
    private Company(
        CompanyId id,
        Name name,
        OrganizationNumber organizationNumber,
        CompanyType companyType)
        : base(id)
    {
        Name = name;
        OrganizationNumber = organizationNumber;
        CompanyType = companyType;
    }

    public Name Name { get; private set; }
    public OrganizationNumber OrganizationNumber { get; private set; }
    public CompanyType CompanyType { get; private set; }
    public DateTime CreatedOnUtc { get; }
    public DateTime ModifiedOnUtc { get; }
    public bool IsDeleted { get; }
    public DateTime DeletedOnUtc { get; }
    public IReadOnlyList<Contact> Contacts => _contacts;
    private readonly List<Contact> _contacts = [];

    public static Result<Company> Create(
        Guid id,
        string name,
        string organizationNumber)
        => Result
            .Combine(
                CompanyId.Create(id),
                Name.Create(name),
                OrganizationNumber.Create(organizationNumber))
            .Map(c => new Company(
                c.Item1,
                c.Item2,
                c.Item3,
                DetermineCompanyType(c.Item3.Value)
                    ?? CompanyType.Unknown))
            .Tap(c => c.RaiseDomainEvent(
                new CompanyCreatedEvent(
                    Guid.NewGuid(),
                    c.Id.Value)));

    public Result UpdateName(string value)
        => Name
            .Create(value)
            .Ensure(
                name => !Name.Equals(name),
                DomainErrors.Name.Identical(value))
            .Tap(name => RaiseDomainEvent(
                new CompanyNameUpdatedEvent(
                    Guid.NewGuid(),
                    Id.Value,
                    Name.Value,
                    name.Value)))
            .Tap(name => Name = name);

    public Result AddContact(
        string firstName,
        string lastName,
        string email)
        => Contact
            .Create(
                Id,
                firstName,
                lastName,
                email)
            .Ensure(
                newContact => !_contacts.Exists(oldContact => oldContact.Email.Equals(newContact.Email)),
                DomainErrors.Contact.EmailAlreadyExist(email))
            .Tap(_contacts.Add);

    public Result RemoveContactByEmail(string email)
        => Email
            .Create(email)
            .Tap(e => _contacts.RemoveAll(c => c.Email.Equals(e)));

    public Result UpdateContactByEmail(
        string email,
        string firstName,
        string lastName)
        => Email
            .Create(email)
            .Bind(FindContactByEmail)
            .Tap(c => c.Update(firstName, lastName));

    private Result<Contact> FindContactByEmail(Email email)
        => Result
            .Create(email)
            .Bind(e => Result.Create(
                _contacts.Find(c => c.Email.Equals(e))))
            .Match(
                contact => contact,
                DomainErrors.Contact.NotFound(email.Value));

    private static CompanyType? DetermineCompanyType(long value)
        => CompanyType.FromValue(
            (int)char.GetNumericValue(
                value.ToString()[0]));
}
