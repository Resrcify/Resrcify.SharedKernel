using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using System.Linq;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetCompanyById;

internal sealed class GetAllCompaniesQueryHandler(
    ICompanyRepository _companyRepository)
    : IQueryHandler<GetCompanyByIdQuery, GetCompanyByIdQueryResponse>
{
    public async Task<Result<GetCompanyByIdQueryResponse>> Handle(
        GetCompanyByIdQuery request,
        CancellationToken cancellationToken)
        => await CompanyId
            .Create(request.CompanyId)
            .Bind(companyId => _companyRepository.GetCompanyAggregateByIdAsync(companyId, cancellationToken))
            .Map(company => new GetCompanyByIdQueryResponse(
                company!.Id.Value,
                company.Name.Value,
                company.OrganizationNumber.Value.ToString(),
                company.Contacts.Select(contact =>
                    new ContactDto(
                        contact.FirstName.Value,
                        contact.LastName.Value,
                        contact.Email.Value))));
}