using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MediatR;
using MediatR.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Messaging.Extensions;
using CustomAbstractions = Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.PerformanceTests.Messaging;

[MemoryDiagnoser]
[RankColumn]
public class MediatorComparisonBenchmarks : IDisposable
{
    private ServiceProvider _customProvider = default!;
    private ServiceProvider _mediatRProvider = default!;

    private CustomAbstractions.ISender _customSender = default!;
    private CustomAbstractions.IPublisher _customPublisher = default!;
    private CustomAbstractions.IStreamSender _customStreamSender = default!;

    private IMediator _mediatRMediator = default!;

    private readonly CustomPing _customPingRequest = new(42);
    private readonly CustomPingValueTask _customPingValueTaskRequest = new(42);
    private readonly MediatRPing _mediatRPingRequest = new(42);
    private readonly CustomPingWithProcessors _customPingWithProcessorsRequest = new(42);
    private readonly CustomPingWithProcessorsValueTask _customPingWithProcessorsValueTaskRequest = new(42);
    private readonly MediatRPingWithProcessors _mediatRPingWithProcessorsRequest = new(42);
    private readonly CustomPingNotification _customNotification = new(7);
    private readonly MediatRPingNotification _mediatRNotification = new(7);
    private readonly CustomStreamPing _customStreamRequest = new(1, 16);
    private readonly MediatRStreamPing _mediatRStreamRequest = new(1, 16);

    private bool _disposed;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _customProvider = BuildCustomProvider();
        _mediatRProvider = BuildMediatRProvider();

        _customSender = _customProvider.GetRequiredService<CustomAbstractions.ISender>();
        _customPublisher = _customProvider.GetRequiredService<CustomAbstractions.IPublisher>();
        _customStreamSender = _customProvider.GetRequiredService<CustomAbstractions.IStreamSender>();

        _mediatRMediator = _mediatRProvider.GetRequiredService<IMediator>();

        _ = await _customSender.Send(_customPingRequest, CancellationToken.None);
        _ = await _customSender.Send(_customPingValueTaskRequest, CancellationToken.None);
        _ = await _mediatRMediator.Send(_mediatRPingRequest, CancellationToken.None);

        _ = await _customSender.Send((object)_customPingRequest, CancellationToken.None);
        _ = await _customSender.Send((object)_customPingValueTaskRequest, CancellationToken.None);
        _ = await _mediatRMediator.Send((object)_mediatRPingRequest, CancellationToken.None);

        _ = await _customSender.Send(_customPingWithProcessorsRequest, CancellationToken.None);
        _ = await _customSender.Send(_customPingWithProcessorsValueTaskRequest, CancellationToken.None);
        _ = await _mediatRMediator.Send(_mediatRPingWithProcessorsRequest, CancellationToken.None);

        await _customPublisher.Publish(_customNotification, CancellationToken.None);
        await _mediatRMediator.Publish(_mediatRNotification, CancellationToken.None);

        _ = await ConsumeAsync(_customStreamSender.CreateStream(_customStreamRequest, CancellationToken.None));
        _ = await ConsumeAsync(_mediatRMediator.CreateStream(_mediatRStreamRequest, CancellationToken.None));
    }

    [Benchmark(Baseline = true)]
    public Task<int> Custom_Send_Typed_Task()
        => _customSender.Send(_customPingRequest, CancellationToken.None);

    [Benchmark]
    public Task<int> Custom_Send_Typed_ValueTask()
        => _customSender.Send(_customPingValueTaskRequest, CancellationToken.None);

    [Benchmark]
    public Task<int> MediatR_Send_Typed()
        => _mediatRMediator.Send(_mediatRPingRequest, CancellationToken.None);

    [Benchmark]
    public Task<object?> Custom_Send_Object_Task()
        => _customSender.Send((object)_customPingRequest, CancellationToken.None);

    [Benchmark]
    public Task<object?> Custom_Send_Object_ValueTask()
        => _customSender.Send((object)_customPingValueTaskRequest, CancellationToken.None);

    [Benchmark]
    public Task<object?> MediatR_Send_Object()
        => _mediatRMediator.Send((object)_mediatRPingRequest, CancellationToken.None);

    [Benchmark]
    public Task<int> Custom_Send_With_PrePost_Task()
        => _customSender.Send(_customPingWithProcessorsRequest, CancellationToken.None);

    [Benchmark]
    public Task<int> Custom_Send_With_PrePost_ValueTask()
        => _customSender.Send(_customPingWithProcessorsValueTaskRequest, CancellationToken.None);

    [Benchmark]
    public Task<int> MediatR_Send_With_PrePost()
        => _mediatRMediator.Send(_mediatRPingWithProcessorsRequest, CancellationToken.None);

    [Benchmark]
    public Task Custom_Publish()
        => _customPublisher.Publish(_customNotification, CancellationToken.None);

    [Benchmark]
    public Task MediatR_Publish()
        => _mediatRMediator.Publish(_mediatRNotification, CancellationToken.None);

    [Benchmark]
    public Task<int> Custom_Stream_ConsumeAll()
        => ConsumeAsync(_customStreamSender.CreateStream(_customStreamRequest, CancellationToken.None));

    [Benchmark]
    public Task<int> MediatR_Stream_ConsumeAll()
        => ConsumeAsync(_mediatRMediator.CreateStream(_mediatRStreamRequest, CancellationToken.None));

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _customProvider.Dispose();
            _mediatRProvider.Dispose();
        }

        _disposed = true;
    }

    private static ServiceProvider BuildCustomProvider()
    {
        var services = new ServiceCollection();

        services.AddMediator(configure => configure
            .RegisterServicesFromAssemblies(typeof(MediatorComparisonBenchmarks).Assembly)
            .AddOpenBehavior(typeof(CustomNoopBehavior<,>)));

        services.AddTransient(typeof(CustomAbstractions.IValueTaskRequestPipelineBehavior<,>), typeof(CustomValueTaskNoopBehavior<,>));

        services.AddTransient(typeof(CustomAbstractions.IStreamPipelineBehavior<,>), typeof(CustomStreamNoopBehavior<,>));

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildMediatRProvider()
    {
        var services = new ServiceCollection();

        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssemblies(typeof(MediatorComparisonBenchmarks).Assembly)
            .AddOpenBehavior(typeof(MediatRNoopBehavior<,>)));

        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(MediatRStreamNoopBehavior<,>));

        return services.BuildServiceProvider();
    }

    private static async Task<int> ConsumeAsync(IAsyncEnumerable<int> stream)
    {
        var sum = 0;

        await foreach (var item in stream)
            sum += item;

        return sum;
    }

    private sealed record CustomPing(int Value) : CustomAbstractions.IRequest<int>;

    private sealed record CustomPingValueTask(int Value) : CustomAbstractions.IRequest<int>;

    private sealed record CustomPingWithProcessors(int Value) : CustomAbstractions.IRequest<int>;

    private sealed record CustomPingWithProcessorsValueTask(int Value) : CustomAbstractions.IRequest<int>;

    private sealed record CustomPingNotification(int Value) : CustomAbstractions.INotification;

    private sealed record CustomStreamPing(int Start, int Count) : CustomAbstractions.IStreamRequest<int>;

    private sealed class CustomPingHandler : CustomAbstractions.IRequestHandler<CustomPing, int>
    {
        public Task<int> Handle(CustomPing request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value + 1);
    }

    private sealed class CustomPingWithProcessorsHandler : CustomAbstractions.IRequestHandler<CustomPingWithProcessors, int>
    {
        public Task<int> Handle(CustomPingWithProcessors request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value + 1);
    }

    private sealed class CustomPingValueTaskHandler : CustomAbstractions.IValueTaskRequestHandler<CustomPingValueTask, int>
    {
        public ValueTask<int> Handle(CustomPingValueTask request, CancellationToken cancellationToken)
            => ValueTask.FromResult(request.Value + 1);
    }

    private sealed class CustomPingWithProcessorsValueTaskHandler : CustomAbstractions.IValueTaskRequestHandler<CustomPingWithProcessorsValueTask, int>
    {
        public ValueTask<int> Handle(CustomPingWithProcessorsValueTask request, CancellationToken cancellationToken)
            => ValueTask.FromResult(request.Value + 1);
    }

    private sealed class CustomNoopBehavior<TRequest, TResponse> : CustomAbstractions.IRequestPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            CustomAbstractions.RequestExecutionDelegate<TRequest, TResponse> next,
            CancellationToken cancellationToken)
            => next(request, cancellationToken);
    }

    private sealed class CustomValueTaskNoopBehavior<TRequest, TResponse> : CustomAbstractions.IValueTaskRequestPipelineBehavior<TRequest, TResponse>
    {
        public ValueTask<TResponse> Handle(
            TRequest request,
            CustomAbstractions.ValueTaskRequestExecutionDelegate<TRequest, TResponse> next,
            CancellationToken cancellationToken)
            => next(request, cancellationToken);
    }

    private sealed class CustomPreProcessor : CustomAbstractions.IRequestPreProcessor<CustomPingWithProcessors>
    {
        public Task Process(CustomPingWithProcessors request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CustomValueTaskPreProcessor : CustomAbstractions.IRequestPreProcessor<CustomPingWithProcessorsValueTask>
    {
        public Task Process(CustomPingWithProcessorsValueTask request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CustomPostProcessor : CustomAbstractions.IRequestPostProcessor<CustomPingWithProcessors, int>
    {
        public Task Process(CustomPingWithProcessors request, int response, CancellationToken cancellationToken)
        {
            _ = request;
            _ = response;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CustomValueTaskPostProcessor : CustomAbstractions.IRequestPostProcessor<CustomPingWithProcessorsValueTask, int>
    {
        public Task Process(CustomPingWithProcessorsValueTask request, int response, CancellationToken cancellationToken)
        {
            _ = request;
            _ = response;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CustomNotificationHandlerOne : CustomAbstractions.INotificationHandler<CustomPingNotification>
    {
        public Task Handle(CustomPingNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CustomNotificationHandlerTwo : CustomAbstractions.INotificationHandler<CustomPingNotification>
    {
        public Task Handle(CustomPingNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CustomNotificationHandlerThree : CustomAbstractions.INotificationHandler<CustomPingNotification>
    {
        public Task Handle(CustomPingNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CustomStreamPingHandler : CustomAbstractions.IStreamRequestHandler<CustomStreamPing, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CustomStreamPing request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var index = 0; index < request.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return request.Start + index;
                await Task.Yield();
            }
        }
    }

    private sealed class CustomStreamNoopBehavior<TRequest, TResponse> : CustomAbstractions.IStreamPipelineBehavior<TRequest, TResponse>
    {
        public IAsyncEnumerable<TResponse> Handle(
            TRequest request,
            CustomAbstractions.StreamHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed record MediatRPing(int Value) : IRequest<int>;

    private sealed record MediatRPingWithProcessors(int Value) : IRequest<int>;

    private sealed record MediatRPingNotification(int Value) : INotification;

    private sealed record MediatRStreamPing(int Start, int Count) : IStreamRequest<int>;

    private sealed class MediatRPingHandler : IRequestHandler<MediatRPing, int>
    {
        public Task<int> Handle(MediatRPing request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value + 1);
    }

    private sealed class MediatRPingWithProcessorsHandler : IRequestHandler<MediatRPingWithProcessors, int>
    {
        public Task<int> Handle(MediatRPingWithProcessors request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value + 1);
    }

    private sealed class MediatRNoopBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class MediatRPreProcessor : IRequestPreProcessor<MediatRPingWithProcessors>
    {
        public Task Process(MediatRPingWithProcessors request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class MediatRPostProcessor : IRequestPostProcessor<MediatRPingWithProcessors, int>
    {
        public Task Process(MediatRPingWithProcessors request, int response, CancellationToken cancellationToken)
        {
            _ = request;
            _ = response;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class MediatRNotificationHandlerOne : INotificationHandler<MediatRPingNotification>
    {
        public Task Handle(MediatRPingNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class MediatRNotificationHandlerTwo : INotificationHandler<MediatRPingNotification>
    {
        public Task Handle(MediatRPingNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class MediatRNotificationHandlerThree : INotificationHandler<MediatRPingNotification>
    {
        public Task Handle(MediatRPingNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class MediatRStreamPingHandler : IStreamRequestHandler<MediatRStreamPing, int>
    {
        public async IAsyncEnumerable<int> Handle(
            MediatRStreamPing request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var index = 0; index < request.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return request.Start + index;
                await Task.Yield();
            }
        }
    }

    private sealed class MediatRStreamNoopBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public IAsyncEnumerable<TResponse> Handle(
            TRequest request,
            StreamHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next();
    }
}
