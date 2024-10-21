using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

namespace Resrcify.SharedKernel.WebApiExample.Presentation.Controllers;

[Route("api/companies")]
public class CompanyController(ISender sender) : ApiController(sender)
{
    [HttpPost()]
    public async Task<IResult> Setting(
        [FromBody] CreateCompanyCommandRequest request,
        CancellationToken cancellationToken = default)
        => await Result
            .Create(new CreateCompanyCommand(
                request.Name,
                request.OrganizationNumber))
            .Bind(request => Sender.Send(request, cancellationToken))
            .Match(Results.NoContent, ToProblemDetails);
}