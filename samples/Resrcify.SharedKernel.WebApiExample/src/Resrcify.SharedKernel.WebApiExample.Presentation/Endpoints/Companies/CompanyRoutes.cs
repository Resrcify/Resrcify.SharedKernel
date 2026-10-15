namespace Resrcify.SharedKernel.WebApiExample.Presentation.Endpoints.Companies;

/// <summary>The company endpoints' routes and OpenAPI tag.</summary>
internal static class CompanyRoutes
{
    public const string Tag = "Companies";
    public const string Companies = "api/companies";
    public const string Company = "api/companies/{companyId:guid}";
    public const string Contacts = "api/companies/{companyId:guid}/contacts";
}
