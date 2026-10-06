using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Repository.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Persistence.Repositories;

internal sealed class CompanyRepository(AppDbContext context)
     : Repository<AppDbContext, Company, CompanyId>(context),
        ICompanyRepository
{
   public Task<Company?> GetCompanyAggregateByIdAsync(
      CompanyId companyId,
      CancellationToken cancellationToken = default)
      => Context.Companies
         .Include(x => x.Contacts)
         .FirstOrDefaultAsync(x => x.Id == companyId, cancellationToken);
}

