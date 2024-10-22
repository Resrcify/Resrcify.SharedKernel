using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.AddContact;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetAllCompanies;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetCompanyById;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.RemoveContact;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateCompanyName;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateContactByEmail;

namespace Resrcify.SharedKernel.WebApiExample.Presentation.Controllers;

[Route("api/companies")]
public class CompanyController(ISender sender) : ApiController(sender)
{
    [HttpPost()]
    public async Task<IResult> CreateCompany(
        [FromBody] CreateCompanyCommandRequest request,
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new CreateCompanyCommand(
                request.Name,
                request.OrganizationNumber))
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.NoContent, ToProblemDetails);

    [HttpGet()]
    public async Task<IResult> GetAllCompanies(
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new GetAllCompaniesQuery())
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.Ok, ToProblemDetails);

    [HttpGet("{companyId}")]
    public async Task<IResult> GetCompanyById(
        [FromRoute] Guid companyId,
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new GetCompanyByIdQuery(companyId))
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.Ok, ToProblemDetails);

    [HttpPatch("{companyId}")]
    public async Task<IResult> UpdateCompanyName(
        [FromRoute] Guid companyId,
        [FromBody] UpdateCompanyNameCommandRequest request,
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new UpdateCompanyNameCommand(companyId, request.Name))
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.NoContent, ToProblemDetails);

    [HttpPost("{companyId}/contacts")]
    public async Task<IResult> AddContact(
        [FromRoute] Guid companyId,
        [FromBody] AddContactCommandRequest request,
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new AddContactCommand(
                companyId,
                request.FirstName,
                request.LastName,
                request.Email))
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.NoContent, ToProblemDetails);

    [HttpDelete("{companyId}/contacts")]
    public async Task<IResult> RemoveContact(
        [FromRoute] Guid companyId,
        [FromBody] RemoveContactCommandRequest request,
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new RemoveContactCommand(
                companyId,
                request.Email))
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.NoContent, ToProblemDetails);

    [HttpPatch("{companyId}/contacts")]
    public async Task<IResult> RemoveContact(
        [FromRoute] Guid companyId,
        [FromBody] UpdateContactByEmailCommandRequest request,
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new UpdateContactByEmailCommand(
                companyId,
                request.NewFirstName,
                request.NewLastName,
                request.Email))
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.NoContent, ToProblemDetails);
}