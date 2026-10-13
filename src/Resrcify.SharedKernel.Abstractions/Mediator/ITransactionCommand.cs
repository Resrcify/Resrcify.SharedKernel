using System;
using System.Data;

namespace Resrcify.SharedKernel.Abstractions.Mediator;

public interface ITransactionCommand
    : ICommand, ITransactionalCommand;

public interface ITransactionCommand<TResponse>
    : ICommand<TResponse>, ITransactionalCommand;

public interface ITransactionalCommand
{
    /// <summary>
    /// The command timeout for the transaction's statements; <see langword="null"/> keeps the DbContext's own (its
    /// configured timeout, e.g. <c>Database:CommandTimeoutInSeconds</c>, or the provider's 30 s).
    /// <see cref="TimeSpan.Zero"/> or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no timeout.
    /// </summary>
    TimeSpan? CommandTimeout { get; }

    /// <summary>
    /// The transaction's isolation level; <see langword="null"/> is read committed. Sent while a transaction is already
    /// open (from a handler the outbox runs), the command joins it and can't ask for a stricter level.
    /// </summary>
    IsolationLevel? IsolationLevel { get; }
}