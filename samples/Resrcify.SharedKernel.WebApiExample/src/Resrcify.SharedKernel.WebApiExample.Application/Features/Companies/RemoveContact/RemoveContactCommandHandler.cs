using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.RemoveContact;

internal sealed class RemoveContactCommandHandler(
    ICompanyRepository _companyRepository)
    : ICommandHandler<RemoveContactCommand>
{
    public async Task<Result> Handle(
        RemoveContactCommand request,
        CancellationToken cancellationToken)
        => await CompanyId
            .Create(request.CompanyId)
            .Bind(companyId => _companyRepository.GetCompanyAggregateByIdAsync(
                companyId,
                cancellationToken))
            .Tap(company => company.RemoveContactByEmail(request.Email));
}
