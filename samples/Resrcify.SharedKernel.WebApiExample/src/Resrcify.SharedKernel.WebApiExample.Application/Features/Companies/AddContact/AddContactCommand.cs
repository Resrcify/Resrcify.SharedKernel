using System;
using Resrcify.SharedKernel.Messaging.Abstractions;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.AddContact;

public sealed record AddContactCommand(
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email)
    : ICommand;