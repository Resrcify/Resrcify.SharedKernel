using System;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

internal sealed class CreateCompanyCommandHandler(ICompanyRepository _companyRepository)
        : ICommandHandler<CreateCompanyCommand>
{
    public async Task<Result> Handle(
        CreateCompanyCommand command,
        CancellationToken cancellationToken)
        => await Company
            .Create(
                Guid.NewGuid(),
                command.Name,
                command.OrganizationNumber)
            .Ensure(
                newCompany => _companyRepository.FirstOrDefaultAsync(
                    oldCompany => oldCompany.OrganizationNumber == newCompany.OrganizationNumber, cancellationToken) is not null,
                DomainErrors.Company.OrganizationNumberAlreadyExist(command.OrganizationNumber))
            .Tap(company => _companyRepository.AddAsync(company, cancellationToken));
}