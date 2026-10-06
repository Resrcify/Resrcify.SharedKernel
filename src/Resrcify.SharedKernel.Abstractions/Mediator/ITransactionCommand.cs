using System;
using System.Data;

namespace Resrcify.SharedKernel.Abstractions.Mediator;

public interface ITransactionCommand
    : ICommand, ITransactionalCommand;

public interface ITransactionCommand<TResponse>
    : ICommand<TResponse>, ITransactionalCommand;

public interface ITransactionalCommand
{
    TimeSpan? CommandTimeout { get; }
    IsolationLevel? IsolationLevel { get; }
}