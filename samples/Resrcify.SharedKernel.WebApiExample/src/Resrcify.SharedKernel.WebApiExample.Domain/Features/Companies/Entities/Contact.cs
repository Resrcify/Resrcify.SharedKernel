using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
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
        => Result
            .Combine(
                ContactId.Create(Guid.NewGuid()),
                Name.Create(firstName),
                Name.Create(lastName),
                Email.Create(email))
            .Map(c => new Contact(
                c.Item1,
                companyId,
                c.Item2,
                c.Item3,
                c.Item4));
    public Result Update(
        string firstName,
        string lastName)
        => Result
            .Combine(
                Name.Create(firstName),
                Name.Create(lastName))
            .Tap(c => FirstName = c.Item1)
            .Tap(c => LastName = c.Item2);


}