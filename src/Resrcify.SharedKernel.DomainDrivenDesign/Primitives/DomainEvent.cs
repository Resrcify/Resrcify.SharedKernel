using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

public abstract record DomainEvent(Guid Id)
    : IDomainEvent;
