using System;
using Resrcify.SharedKernel.Abstractions;

namespace Resrcify.SharedKernel.Primitives;

public abstract record DomainEvent(Guid Id) : IDomainEvent;
