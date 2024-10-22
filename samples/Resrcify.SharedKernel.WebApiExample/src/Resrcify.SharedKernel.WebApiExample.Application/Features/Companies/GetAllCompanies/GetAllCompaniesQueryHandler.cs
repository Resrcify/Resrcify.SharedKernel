using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetAllCompanies;

internal sealed class GetAllCompaniesQueryHandler(
    ICompanyRepository _companyRepository)
    : IQueryHandler<GetAllCompaniesQuery, GetAllCompaniesQueryResponse>
{
    public async Task<Result<GetAllCompaniesQueryResponse>> Handle(
        GetAllCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        var allCompanies = await _companyRepository.GetAllAsync(cancellationToken);
        var companyDtos = allCompanies.Select(company => new CompanyDto(
            company.Id.Value,
            company.Name.Value,
            company.OrganizationNumber.Value.ToString()));
        return new GetAllCompaniesQueryResponse(companyDtos);
    }
}