using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetAllCompanies;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetCompanyById;

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
}