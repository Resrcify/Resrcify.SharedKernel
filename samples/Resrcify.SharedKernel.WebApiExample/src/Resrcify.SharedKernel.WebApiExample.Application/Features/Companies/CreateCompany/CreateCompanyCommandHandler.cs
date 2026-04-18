using System;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.Repositories;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

internal sealed class CreateCompanyCommandHandler(
    ICompanyRepository _companyRepository)
        : ICommandHandler<CreateCompanyCommand>
{
    public async Task<Result> Handle(
        CreateCompanyCommand command,
        CancellationToken cancellationToken)
    {
        var newCompany = Company.Create(
            Guid.NewGuid(),
            command.Name,
            command.OrganizationNumber);

        if (newCompany.IsFailure)
            return newCompany;

        var oldCompany = await FindByOrganizationNumberAsync(
            newCompany.Value.OrganizationNumber,
            cancellationToken);

        if (oldCompany is not null)
            return DomainErrors.Company.OrganizationNumberAlreadyExist(command.OrganizationNumber);

        await _companyRepository.AddAsync(newCompany.Value, cancellationToken);

        return Result.Success();
    }

    private async Task<Company?> FindByOrganizationNumberAsync(
        Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects.OrganizationNumber organizationNumber,
        CancellationToken cancellationToken)
    {
        await foreach (var company in _companyRepository.GetAllAsync())
        {
            if (company.OrganizationNumber == organizationNumber)
            {
                return company;
            }
        }

        return null;
    }
}
