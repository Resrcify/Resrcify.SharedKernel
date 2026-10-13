using System;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateCompanyName;

public sealed record UpdateCompanyNameCommand(
    Guid CompanyId,
    string Name)
    : ICommand;