using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Entities;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Persistence.Configurations.Companies;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder
            .ToTable("Contacts");

        builder
            .HasKey(x => x.Id);

        builder
            .Property(x => x.Id)
            .HasConversion(x => x.Value, v => ContactId.Create(v).Value)
            .ValueGeneratedNever();

        builder
            .Property(x => x.CompanyId)
            .HasConversion(x => x.Value, v => CompanyId.Create(v).Value)
            .ValueGeneratedNever();

        builder
            .Property(x => x.FirstName)
            .HasConversion(x => x.Value, v => Name.Create(v).Value)
            .HasMaxLength(Name.MaxLength)
            .IsRequired();

        builder
            .Property(x => x.LastName)
            .HasConversion(x => x.Value, v => Name.Create(v).Value)
            .HasMaxLength(Name.MaxLength)
            .IsRequired();

        builder
            .Property(x => x.Email)
            .HasConversion(x => x.Value, v => Email.Create(v).Value)
            .HasMaxLength(Name.MaxLength)
            .IsRequired();

        builder
            .Property(x => x.CreatedOnUtc)
            .IsRequired();

        builder
            .Property(x => x.ModifiedOnUtc)
            .IsRequired();
    }
}
