using System;
using System.Data;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ITransactionCommand
    : ICommand, ITransactionalCommand;

public interface ITransactionCommand<TResponse>
    : ICommand<TResponse>, ITransactionalCommand;

public interface ITransactionalCommand
{
    TimeSpan? CommandTimeout { get; }
    IsolationLevel? IsolationLevel { get; }
}