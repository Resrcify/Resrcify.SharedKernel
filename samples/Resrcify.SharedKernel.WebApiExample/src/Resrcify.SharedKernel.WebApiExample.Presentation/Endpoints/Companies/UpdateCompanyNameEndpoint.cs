using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Resrcify.SharedKernel.Web.Abstractions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateCompanyName;

namespace Resrcify.SharedKernel.WebApiExample.Presentation.Endpoints.Companies;

/// <summary>Sends the request through the mediator and answers its result (200 or 204, or problem details).</summary>
public sealed class UpdateCompanyNameEndpoint
    : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
        => app.MapPatchRequest(
                CompanyRoutes.Company,
                (Guid companyId, UpdateCompanyNameCommandRequest body) => new UpdateCompanyNameCommand(companyId, body.Name))
            .WithName("Companies.UpdateName")
            .WithTags(CompanyRoutes.Tag);
}
