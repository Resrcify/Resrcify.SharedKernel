using System;
using System.Data;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ITransactionCommand
    : ICommand, ITransactional;

public interface ITransactionCommand<TResponse>
    : ICommand<TResponse>, ITransactional;

public interface ITransactional
{
    TimeSpan? CommandTimeout { get; }
    IsolationLevel? IsolationLevel { get; }
}