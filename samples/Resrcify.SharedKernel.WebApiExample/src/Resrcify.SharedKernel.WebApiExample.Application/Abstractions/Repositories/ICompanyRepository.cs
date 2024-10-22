using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Repository.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetCompanyById;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
public interface ICompanyRepository
    : IRepository<Company, CompanyId>
{
    Task<Result<GetCompanyByIdQueryResponse>> GetCompanyAggregateByIdAsync(
        CompanyId companyId,
        CancellationToken cancellationToken = default);
}