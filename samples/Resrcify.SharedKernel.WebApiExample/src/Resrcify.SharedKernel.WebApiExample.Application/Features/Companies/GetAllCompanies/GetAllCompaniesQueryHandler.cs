using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Results.Primitives;
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
        var allCompaniesMaterialized = new List<Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Company>();

        await foreach (var company in _companyRepository.GetAllAsync())
        {
            allCompaniesMaterialized.Add(company);
        }

        var companyDtos = allCompaniesMaterialized.Select(company => new CompanyDto(
            company.Id.Value,
            company.Name.Value,
            company.OrganizationNumber.Value.ToString()));
        return new GetAllCompaniesQueryResponse(companyDtos);
    }
}
