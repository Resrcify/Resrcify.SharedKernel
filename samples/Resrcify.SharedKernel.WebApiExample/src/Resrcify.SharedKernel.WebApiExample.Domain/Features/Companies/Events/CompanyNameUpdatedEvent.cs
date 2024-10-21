using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Events;

public sealed record CompanyNameUpdatedEvent(
    Guid Id,
    Guid CompanyId,
    string OldName,
    string NewName)
    : DomainEvent(Id);