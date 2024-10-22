using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Repository.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetCompanyById;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Persistence.Repositories;

internal sealed class CompanyRepository(AppDbContext context)
     : Repository<AppDbContext, Company, CompanyId>(context),
        ICompanyRepository
{
   public async Task<Result<GetCompanyByIdQueryResponse>> GetCompanyAggregateByIdAsync(
      CompanyId companyId,
      CancellationToken cancellationToken = default)
      => Result
         .Create(
            await Context.Companies
               .Include(x => x.Contacts)
               .FirstOrDefaultAsync(x => x.Id == companyId, cancellationToken))
         .Match(
            company => company,
            DomainErrors.Company.NotFound(companyId.Value))
         .Map(company => new GetCompanyByIdQueryResponse(
            company.Id.Value,
            company.Name.Value,
            company.OrganizationNumber.Value.ToString(),
            company.Contacts.Select(contact => new ContactDto(
               contact.FirstName.Value,
               contact.LastName.Value,
               contact.Email.Value))));
}
