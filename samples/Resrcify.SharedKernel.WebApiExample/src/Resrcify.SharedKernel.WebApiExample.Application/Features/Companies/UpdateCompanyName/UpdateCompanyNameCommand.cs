using System;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateCompanyName;

public sealed record UpdateCompanyNameCommand(
    Guid CompanyId,
    string Name)
    : ICommand;