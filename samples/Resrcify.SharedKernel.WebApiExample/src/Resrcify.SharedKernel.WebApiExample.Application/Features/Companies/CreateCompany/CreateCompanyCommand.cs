using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

public sealed record CreateCompanyCommand(
    string Name,
    string OrganizationNumber)
    : ICommand;