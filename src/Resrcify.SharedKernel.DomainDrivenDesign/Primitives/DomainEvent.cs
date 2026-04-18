using System;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

public abstract record DomainEvent(
    Guid Id)
    : IDomainEvent;
