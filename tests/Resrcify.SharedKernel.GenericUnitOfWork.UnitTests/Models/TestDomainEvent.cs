using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
namespace Resrcify.SharedKernel.GenericUnitOfWork.UnitTests.Models;

public record TestDomainEvent(Guid Id) : DomainEvent(Id);