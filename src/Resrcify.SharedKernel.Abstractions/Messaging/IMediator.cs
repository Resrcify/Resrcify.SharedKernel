using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Abstractions.Messaging;

public interface IRequest<out TResponse>
{
    static Type ResponseType => typeof(TResponse);
}

public interface INotification
{
}

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IValueTaskRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface INotificationHandler<in TNotification>
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

public interface IStreamRequest<out TResponse>
{
    static Type ResponseType => typeof(TResponse);
}

public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);
public delegate ValueTask<TResponse> ValueTaskRequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);
public delegate Task<TResponse> RequestExecutionDelegate<in TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default);
public delegate ValueTask<TResponse> ValueTaskRequestExecutionDelegate<in TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default);
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>(CancellationToken cancellationToken = default);

public interface IPipelineBehavior<in TRequest, TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

public interface IValueTaskPipelineBehavior<in TRequest, TResponse>
{
    ValueTask<TResponse> Handle(
        TRequest request,
        ValueTaskRequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

public interface IRequestPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestExecutionDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken);
}

public interface IValueTaskRequestPipelineBehavior<TRequest, TResponse>
{
    ValueTask<TResponse> Handle(
        TRequest request,
        ValueTaskRequestExecutionDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken);
}

public interface IStreamPipelineBehavior<in TRequest, TResponse>
{
    IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

public interface IRequestPreProcessor<in TRequest>
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}

public interface IRequestPostProcessor<in TRequest, in TResponse>
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}

public interface IStreamSender
{
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task<object?> Send(object request, CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    Task Publish(object notification, CancellationToken cancellationToken = default);
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
    where TNotification : notnull;
}

public interface IMediator
    : ISender,
    IPublisher,
    IStreamSender;
