using Resrcify.SharedKernel.Messaging.Abstractions;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CreateCompany;

public sealed record CreateCompanyCommand(
    string Name,
    string OrganizationNumber)
    : ICommand;