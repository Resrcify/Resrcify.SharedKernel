using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Enums;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Persistence.Configurations.Companies;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder
            .ToTable("Companies");

        builder
            .HasKey(x => x.Id);

        builder
            .Property(x => x.Id)
            .HasConversion(x => x.Value, v => CompanyId.Create(v).Value)
            .ValueGeneratedNever();

        builder
            .Property(x => x.Name)
            .HasConversion(x => x.Value, v => Name.Create(v).Value)
            .HasMaxLength(Name.MaxLength)
            .IsRequired();

        builder
            .Property(x => x.OrganizationNumber)
            .HasConversion(x => x.Value, v => OrganizationNumber.Create(v.ToString()).Value)
            .IsRequired();

        builder
            .Property(x => x.CompanyType)
            .HasConversion(
                x => x.Value,
                v => CompanyType.FromValue(v)!)
            .IsRequired();

        builder
            .Property(x => x.CreatedOnUtc)
            .IsRequired();

        builder
            .Property(x => x.ModifiedOnUtc)
            .IsRequired();

        builder
            .Property(x => x.DeletedOnUtc);

        builder
            .Property(x => x.IsDeleted)
            .IsRequired();

        builder
            .HasMany(x => x.Contacts)
            .WithOne()
            .HasForeignKey(x => x.CompanyId);

        builder
            .Metadata
                .FindNavigation(nameof(Company.Contacts))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
