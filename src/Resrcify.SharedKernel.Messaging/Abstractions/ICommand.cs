﻿using MediatR;
using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ICommand
    : IRequest<Result>, IBaseCommand;

public interface ICommand<TResponse>
    : IRequest<Result<TResponse>>, IBaseCommand;

public interface IBaseCommand;