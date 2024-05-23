using MediatR;
using Resrcify.SharedKernel.ResultFramework.Shared;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ICommand
    : IRequest<Result>;

public interface ICommand<TResponse>
    : IRequest<Result<TResponse>>;
