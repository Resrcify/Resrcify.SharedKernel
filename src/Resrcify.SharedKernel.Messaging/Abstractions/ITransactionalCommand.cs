using System;
using System.Data;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ITransactionalCommand : ICommand
{
    TimeSpan? CommandTimeout { get; }
    IsolationLevel? IsolationLevel { get; }
}

public interface ITransactionalCommand<TResponse> : ICommand<TResponse>
{
    TimeSpan? CommandTimeout { get; }
    IsolationLevel? IsolationLevel { get; }
}
