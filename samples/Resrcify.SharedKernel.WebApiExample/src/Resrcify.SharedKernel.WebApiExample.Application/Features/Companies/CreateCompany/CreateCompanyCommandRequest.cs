namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

public sealed record CreateCompanyCommandRequest(
    string Name,
    string OrganizationNumber);