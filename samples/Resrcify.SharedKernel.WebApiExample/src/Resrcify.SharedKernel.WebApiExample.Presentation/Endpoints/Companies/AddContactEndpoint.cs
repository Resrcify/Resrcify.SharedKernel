using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Resrcify.SharedKernel.Web.Abstractions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.AddContact;

namespace Resrcify.SharedKernel.WebApiExample.Presentation.Endpoints.Companies;

/// <summary>Sends the request through the mediator and answers its result (200 or 204, or problem details).</summary>
public sealed class AddContactEndpoint
    : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
        => app.MapPostRequest(
                CompanyRoutes.Contacts,
                (Guid companyId, AddContactCommandRequest body) =>
                new AddContactCommand(companyId, body.FirstName, body.LastName, body.Email))
            .WithName("Companies.AddContact")
            .WithTags(CompanyRoutes.Tag);
}
