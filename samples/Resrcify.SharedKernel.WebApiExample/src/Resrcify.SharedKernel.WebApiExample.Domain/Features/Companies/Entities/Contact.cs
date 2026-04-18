using System;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Entities;

public sealed class Contact
    : Entity<ContactId>,
    IAuditableEntity
{
    private Contact(
        ContactId id,
        CompanyId companyId,
        Name firstName,
        Name lastName,
        Email email)
        : base(id)
    {
        CompanyId = companyId;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
    }
    public CompanyId CompanyId { get; private set; }
    public Name FirstName { get; private set; }
    public Name LastName { get; private set; }
    public Email Email { get; private set; }
    public DateTime CreatedOnUtc { get; }
    public DateTime ModifiedOnUtc { get; }

    public static Result<Contact> Create(
        CompanyId companyId,
        string firstName,
        string lastName,
        string email)
    {
        var contactIdResult = ContactId.Create(Guid.NewGuid());
        var firstNameResult = Name.Create(firstName);
        var lastNameResult = Name.Create(lastName);
        var emailResult = Email.Create(email);

        return Result.Combine(
            () => new Contact(
                contactIdResult.Value,
                companyId,
                firstNameResult.Value,
                lastNameResult.Value,
                emailResult.Value),
            contactIdResult,
            firstNameResult,
            lastNameResult,
            emailResult);
    }

    public Result Update(
        string firstName,
        string lastName)
    {
        var firstNameResult = Name.Create(firstName);
        var lastNameResult = Name.Create(lastName);

        return Result.Combine(
                () => Result.Success(),
                firstNameResult,
                lastNameResult)
            .Tap(_ => FirstName = firstNameResult.Value)
            .Tap(_ => LastName = lastNameResult.Value);
    }


}
