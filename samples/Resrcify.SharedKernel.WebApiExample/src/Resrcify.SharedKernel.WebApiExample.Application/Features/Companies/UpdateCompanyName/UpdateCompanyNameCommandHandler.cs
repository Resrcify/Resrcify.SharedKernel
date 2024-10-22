using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateCompanyName;

internal sealed class UpdateCompanyNameCommandHandler(
    ICompanyRepository _companyRepository)
    : ICommandHandler<UpdateCompanyNameCommand>
{
    public async Task<Result> Handle(
        UpdateCompanyNameCommand request,
        CancellationToken cancellationToken)
        => await CompanyId
            .Create(request.CompanyId)
            .Bind(companyId => _companyRepository.GetCompanyAggregateByIdAsync(
                companyId,
                cancellationToken))
            .Tap(company => company.UpdateName(request.Name));
}