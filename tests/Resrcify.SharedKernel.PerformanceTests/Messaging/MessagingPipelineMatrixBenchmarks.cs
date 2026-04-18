using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Extensions;
using Resrcify.SharedKernel.Messaging.Publishing;

namespace Resrcify.SharedKernel.PerformanceTests.Messaging;

[MemoryDiagnoser]
public class MessagingPipelineMatrixBenchmarks : IDisposable
{
    [Params(0, 1, 3)]
    public int BehaviorCount { get; set; }

    [Params(NotificationPublishStrategy.Sequential, NotificationPublishStrategy.Parallel)]
    public NotificationPublishStrategy PublishStrategy { get; set; }

    private ServiceProvider _provider = default!;
    private ISender _sender = default!;
    private IPublisher _publisher = default!;

    private readonly MatrixRequest _request = new(21);
    private readonly MatrixNotification _notification = new(7);

    private bool _disposed;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _provider = BuildProvider();
        _sender = _provider.GetRequiredService<ISender>();
        _publisher = _provider.GetRequiredService<IPublisher>();

        _ = await _sender.Send(_request, CancellationToken.None);
        await _publisher.Publish(_notification, CancellationToken.None);
    }

    [Benchmark(Baseline = true)]
    public Task<int> Custom_Send_Typed_Matrix()
        => _sender.Send(_request, CancellationToken.None);

    [Benchmark]
    public Task Custom_Publish_Matrix()
        => _publisher.Publish(_notification, CancellationToken.None);

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
            _provider.Dispose();

        _disposed = true;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddMediator(configure =>
        {
            configure
                .RegisterServicesFromAssemblies(typeof(MessagingPipelineMatrixBenchmarks).Assembly)
                .UseNotificationPublishStrategy(PublishStrategy);

            if (BehaviorCount >= 1)
                configure.AddOpenBehavior(typeof(NoopBehaviorOne<,>));

            if (BehaviorCount >= 2)
                configure.AddOpenBehavior(typeof(NoopBehaviorTwo<,>));

            if (BehaviorCount >= 3)
                configure.AddOpenBehavior(typeof(NoopBehaviorThree<,>));
        });

        return services.BuildServiceProvider();
    }

    private sealed record MatrixRequest(int Number) : IRequest<int>;

    private sealed class MatrixRequestHandler : IRequestHandler<MatrixRequest, int>
    {
        public Task<int> Handle(MatrixRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Number + 1);
    }

    private sealed class NoopBehaviorOne<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestExecutionDelegate<TRequest, TResponse> next,
            CancellationToken cancellationToken)
            => next(request, cancellationToken);
    }

    private sealed class NoopBehaviorTwo<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestExecutionDelegate<TRequest, TResponse> next,
            CancellationToken cancellationToken)
            => next(request, cancellationToken);
    }

    private sealed class NoopBehaviorThree<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestExecutionDelegate<TRequest, TResponse> next,
            CancellationToken cancellationToken)
            => next(request, cancellationToken);
    }

    private sealed record MatrixNotification(int Number) : INotification;

    private sealed class MatrixNotificationHandlerOne : INotificationHandler<MatrixNotification>
    {
        public Task Handle(MatrixNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class MatrixNotificationHandlerTwo : INotificationHandler<MatrixNotification>
    {
        public Task Handle(MatrixNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class MatrixNotificationHandlerThree : INotificationHandler<MatrixNotification>
    {
        public Task Handle(MatrixNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
