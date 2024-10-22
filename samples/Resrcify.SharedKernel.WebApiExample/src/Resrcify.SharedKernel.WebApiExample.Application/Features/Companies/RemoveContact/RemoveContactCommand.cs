using System;
using Resrcify.SharedKernel.Messaging.Abstractions;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.RemoveContact;

public sealed record RemoveContactCommand(
    Guid CompanyId,
    string Email)
    : ICommand;