using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.AddContact;

internal sealed class AddContactCommandHandler(
    ICompanyRepository _companyRepository)
    : ICommandHandler<AddContactCommand>
{
    public async Task<Result> Handle(
        AddContactCommand request,
        CancellationToken cancellationToken)
        => await CompanyId
            .Create(request.CompanyId)
            .Bind(companyId => _companyRepository.GetCompanyAggregateByIdAsync(
                companyId,
                cancellationToken))
            .Tap(company => company.AddContact(
                request.FirstName,
                request.LastName,
                request.Email));
}