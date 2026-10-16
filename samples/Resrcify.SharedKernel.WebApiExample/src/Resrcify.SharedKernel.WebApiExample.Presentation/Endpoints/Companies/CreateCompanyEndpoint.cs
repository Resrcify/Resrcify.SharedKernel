using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Resrcify.SharedKernel.Web.Abstractions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

namespace Resrcify.SharedKernel.WebApiExample.Presentation.Endpoints.Companies;

/// <summary>Sends the request through the mediator and answers its result (200 or 204, or problem details).</summary>
public sealed class CreateCompanyEndpoint
    : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
        => app.MapPostRequest(
                CompanyRoutes.Companies,
                (CreateCompanyCommandRequest body) => new CreateCompanyCommand(body.Name, body.OrganizationNumber))
            .WithName("Companies.Create")
            .WithTags(CompanyRoutes.Tag);
}
