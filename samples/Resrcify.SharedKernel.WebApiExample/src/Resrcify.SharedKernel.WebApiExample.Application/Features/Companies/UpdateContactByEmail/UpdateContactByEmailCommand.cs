using System;
using Resrcify.SharedKernel.Messaging.Abstractions;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateContactByEmail;

public sealed record UpdateContactByEmailCommand(
    Guid CompanyId,
    string NewFirstName,
    string NewLastName,
    string Email)
    : ICommand;