using System;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.RemoveContact;

public sealed record RemoveContactCommand(
    Guid CompanyId,
    string Email)
    : ICommand;