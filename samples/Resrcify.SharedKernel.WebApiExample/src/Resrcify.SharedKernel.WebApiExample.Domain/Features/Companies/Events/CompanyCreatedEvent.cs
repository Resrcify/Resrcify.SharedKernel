using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Events;

public sealed record CompanyCreatedEvent(
    Guid Id,
    Guid CompanyId)
    : DomainEvent(Id);