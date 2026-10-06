using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Repository;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
public interface ICompanyRepository
    : IRepository<Company, CompanyId>
{
    Task<Company?> GetCompanyAggregateByIdAsync(
        CompanyId companyId,
        CancellationToken cancellationToken = default);
}
