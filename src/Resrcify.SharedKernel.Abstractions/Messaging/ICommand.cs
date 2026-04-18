using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.Messaging;

public interface ICommand
    : IRequest<Result>, IBaseCommand;

public interface ICommand<TResponse>
    : IRequest<Result<TResponse>>, IBaseCommand;

public interface IBaseCommand;
