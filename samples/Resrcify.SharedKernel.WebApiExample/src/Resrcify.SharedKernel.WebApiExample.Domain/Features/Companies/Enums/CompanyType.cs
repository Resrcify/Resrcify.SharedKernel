using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Enums;

public sealed class CompanyType : Enumeration<CompanyType>
{
    public static readonly CompanyType Unknown = new(0, "Unknown");
    public static readonly CompanyType DeceasedEstate = new(1, "Deceased estate");
    public static readonly CompanyType GovernmentEntity = new(2, "Government entities (State, region, municipalitie, parish)");
    public static readonly CompanyType ForeignCompany = new(3, "Foreign company");
    public static readonly CompanyType LimitedCompany = new(5, "Limited company");
    public static readonly CompanyType EconomicAssociation = new(7, "Economic association, housing cooperative, and community association");
    public static readonly CompanyType NonProfitOrganization = new(8, "Non-profit organization");
    public static readonly CompanyType Foundations = new(9, "Foundation");

    private CompanyType(int value, string name)
    : base(value, name)
    {
    }
}
