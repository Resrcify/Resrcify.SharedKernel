using MediatR;
using Resrcify.SharedKernel.ResultFramework.Shared;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ICommandHandler<TCommand>
    : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<TCommand, TResponse>
    : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;
