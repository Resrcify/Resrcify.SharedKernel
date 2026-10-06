using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateContactByEmail;

internal sealed class UpdateContactByEmailCommandHandler(
    ICompanyRepository _companyRepository)
    : ICommandHandler<UpdateContactByEmailCommand>
{
    public async Task<Result> Handle(
        UpdateContactByEmailCommand request,
        CancellationToken cancellationToken)
        => await CompanyId
            .Create(request.CompanyId)
            .Bind(companyId => _companyRepository.GetCompanyAggregateByIdAsync(
                companyId,
                cancellationToken)
                .ToResultAsync(DomainErrors.Company.NotFound(companyId.Value)))
            .Tap(company => company.UpdateContactByEmail(
                request.Email,
                request.NewFirstName,
                request.NewLastName));
}
