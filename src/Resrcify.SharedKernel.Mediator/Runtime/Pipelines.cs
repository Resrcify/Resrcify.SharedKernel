using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Runtime;

// A request's behaviors around its handler, walked by index on each call. Nothing is built ahead: a runtime is
// usually built for one call (the mediator is transient), so a chain of closures composed up front cost two closures
// and two delegates per behavior that served a single call. Each call makes only the `next` its behaviors are handed.
// Order: the first IPipelineBehavior is the outermost, then the request behaviors (first outermost), then the handler.

/// <summary>The pipeline of a request with a <see cref="Task{TResult}"/> handler.</summary>
internal sealed class TaskPipeline<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> handler,
    IPipelineBehavior<TRequest, TResponse>[] behaviors,
    IRequestPipelineBehavior<TRequest, TResponse>[] requestBehaviors)
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Invoke(TRequest request, CancellationToken cancellationToken)
        => Behavior(0, request, cancellationToken);

    private Task<TResponse> Behavior(int index, TRequest request, CancellationToken cancellationToken)
    {
        if (index == behaviors.Length)
            return RequestBehavior(0, request, cancellationToken);

        var next = new BehaviorNext(this, request, index + 1);
        return behaviors[index].Handle(request, next.Invoke, cancellationToken);
    }

    private Task<TResponse> RequestBehavior(int index, TRequest request, CancellationToken cancellationToken)
    {
        if (index == requestBehaviors.Length)
            return handler.Handle(request, cancellationToken);

        var next = new RequestBehaviorNext(this, index + 1);
        return requestBehaviors[index].Handle(request, next.Invoke, cancellationToken);
    }

    private sealed class BehaviorNext(TaskPipeline<TRequest, TResponse> pipeline, TRequest request, int index)
    {
        public Task<TResponse> Invoke(CancellationToken cancellationToken)
            => pipeline.Behavior(index, request, cancellationToken);
    }

    private sealed class RequestBehaviorNext(TaskPipeline<TRequest, TResponse> pipeline, int index)
    {
        public Task<TResponse> Invoke(TRequest request, CancellationToken cancellationToken)
            => pipeline.RequestBehavior(index, request, cancellationToken);
    }
}

/// <summary>The pipeline of a request with a <see cref="ValueTask{TResult}"/> handler.</summary>
internal sealed class ValueTaskPipeline<TRequest, TResponse>(
    IValueTaskRequestHandler<TRequest, TResponse> handler,
    IValueTaskPipelineBehavior<TRequest, TResponse>[] behaviors,
    IValueTaskRequestPipelineBehavior<TRequest, TResponse>[] requestBehaviors)
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Invoke(TRequest request, CancellationToken cancellationToken)
        => Behavior(0, request, cancellationToken);

    private ValueTask<TResponse> Behavior(int index, TRequest request, CancellationToken cancellationToken)
    {
        if (index == behaviors.Length)
            return RequestBehavior(0, request, cancellationToken);

        var next = new BehaviorNext(this, request, index + 1);
        return behaviors[index].Handle(request, next.Invoke, cancellationToken);
    }

    private ValueTask<TResponse> RequestBehavior(int index, TRequest request, CancellationToken cancellationToken)
    {
        if (index == requestBehaviors.Length)
            return handler.Handle(request, cancellationToken);

        var next = new RequestBehaviorNext(this, index + 1);
        return requestBehaviors[index].Handle(request, next.Invoke, cancellationToken);
    }

    private sealed class BehaviorNext(ValueTaskPipeline<TRequest, TResponse> pipeline, TRequest request, int index)
    {
        public ValueTask<TResponse> Invoke(CancellationToken cancellationToken)
            => pipeline.Behavior(index, request, cancellationToken);
    }

    private sealed class RequestBehaviorNext(ValueTaskPipeline<TRequest, TResponse> pipeline, int index)
    {
        public ValueTask<TResponse> Invoke(TRequest request, CancellationToken cancellationToken)
            => pipeline.RequestBehavior(index, request, cancellationToken);
    }
}

/// <summary>The pipeline of a stream request.</summary>
internal sealed class StreamPipeline<TRequest, TResponse>(
    IStreamRequestHandler<TRequest, TResponse> handler,
    IStreamPipelineBehavior<TRequest, TResponse>[] behaviors)
    where TRequest : IStreamRequest<TResponse>
{
    public IAsyncEnumerable<TResponse> Invoke(TRequest request, CancellationToken cancellationToken)
        => Behavior(0, request, cancellationToken);

    private IAsyncEnumerable<TResponse> Behavior(int index, TRequest request, CancellationToken cancellationToken)
    {
        if (index == behaviors.Length)
            return handler.Handle(request, cancellationToken);

        var next = new BehaviorNext(this, request, index + 1);
        return behaviors[index].Handle(request, next.Invoke, cancellationToken);
    }

    private sealed class BehaviorNext(StreamPipeline<TRequest, TResponse> pipeline, TRequest request, int index)
    {
        public IAsyncEnumerable<TResponse> Invoke(CancellationToken cancellationToken)
            => pipeline.Behavior(index, request, cancellationToken);
    }
}
