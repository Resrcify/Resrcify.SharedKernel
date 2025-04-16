using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

internal sealed record TestDomainEvent(
    Guid Id,
    string Message) : DomainEvent(Id);