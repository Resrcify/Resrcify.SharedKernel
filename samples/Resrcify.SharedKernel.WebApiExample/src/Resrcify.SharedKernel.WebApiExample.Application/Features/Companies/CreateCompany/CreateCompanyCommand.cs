using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

public sealed record CreateCompanyCommand(
    string Name,
    string OrganizationNumber)
    : ICommand;