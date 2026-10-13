using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Mediator.Extensions;
using CustomAbstractions = Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.PerformanceTests.Mediator;

/// <summary>
/// A send the way a service does one: a new DI scope per request (an HTTP request, a message, a job run), the sender
/// resolved from it, five behaviors in the pipeline (as a service's logging, validation, transaction, unit of work and
/// caching). <see cref="MediatorComparisonBenchmarks"/> reuses one warm sender, which production rarely does: the
/// mediator is transient, so each scope gets a new one.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class MediatorPerScopeBenchmarks : IDisposable
{
    private ServiceProvider _composedProvider = default!;
    private ServiceProvider _uncomposedProvider = default!;
    private ServiceProvider _mediatRProvider = default!;
    private CustomAbstractions.ISender _warmSender = default!;

    private readonly ScopedPing _customRequest = new(42);
    private readonly MediatRScopedPing _mediatRRequest = new(42);

    private bool _disposed;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _composedProvider = BuildCustomProvider(composeAtDiTime: true);
        _uncomposedProvider = BuildCustomProvider(composeAtDiTime: false);
        _mediatRProvider = BuildMediatRProvider();
        _warmSender = _composedProvider.GetRequiredService<CustomAbstractions.ISender>();

        _ = await Custom_PerScope_Composed();
        _ = await Custom_PerScope_Uncomposed();
        _ = await MediatR_PerScope();
        _ = await Custom_Warm_Composed();
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Custom_PerScope_Composed()
    {
        await using var scope = _composedProvider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<CustomAbstractions.ISender>();
        return await sender.Send(_customRequest, CancellationToken.None);
    }

    [Benchmark]
    public async Task<int> Custom_PerScope_Uncomposed()
    {
        await using var scope = _uncomposedProvider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<CustomAbstractions.ISender>();
        return await sender.Send(_customRequest, CancellationToken.None);
    }

    [Benchmark]
    public async Task<int> MediatR_PerScope()
    {
        await using var scope = _mediatRProvider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(_mediatRRequest, CancellationToken.None);
    }

    [Benchmark]
    public Task<int> Custom_Warm_Composed()
        => _warmSender.Send(_customRequest, CancellationToken.None);

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
            _composedProvider?.Dispose();
            _uncomposedProvider?.Dispose();
            _mediatRProvider?.Dispose();
        }

        _disposed = true;
    }

    private static ServiceProvider BuildCustomProvider(bool composeAtDiTime)
    {
        var services = new ServiceCollection();
        services.AddMediator(configure =>
        {
            if (composeAtDiTime)
                configure.EnableDiTimePipelineComposition();
            // No assembly scan: it would also register every other benchmark's open behaviors from this assembly.
            configure
                .AddOpenBehavior(typeof(NoopBehavior1<,>))
                .AddOpenBehavior(typeof(NoopBehavior2<,>))
                .AddOpenBehavior(typeof(NoopBehavior3<,>))
                .AddOpenBehavior(typeof(NoopBehavior4<,>))
                .AddOpenBehavior(typeof(NoopBehavior5<,>));
        });
        services.AddTransient<CustomAbstractions.IRequestHandler<ScopedPing, int>, ScopedPingHandler>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildMediatRProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssemblies(typeof(MediatorPerScopeBenchmarks).Assembly)
            .AddOpenBehavior(typeof(MediatRNoopBehavior1<,>))
            .AddOpenBehavior(typeof(MediatRNoopBehavior2<,>))
            .AddOpenBehavior(typeof(MediatRNoopBehavior3<,>))
            .AddOpenBehavior(typeof(MediatRNoopBehavior4<,>))
            .AddOpenBehavior(typeof(MediatRNoopBehavior5<,>)));
        return services.BuildServiceProvider();
    }

    private sealed record ScopedPing(int Value) : CustomAbstractions.IRequest<int>;

    private sealed record MediatRScopedPing(int Value) : IRequest<int>;

    private sealed class ScopedPingHandler : CustomAbstractions.IRequestHandler<ScopedPing, int>
    {
        public Task<int> Handle(ScopedPing request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    private sealed class MediatRScopedPingHandler : IRequestHandler<MediatRScopedPing, int>
    {
        public Task<int> Handle(MediatRScopedPing request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    private sealed class NoopBehavior1<TRequest, TResponse> : CustomAbstractions.IPipelineBehavior<TRequest, TResponse>
        where TRequest : CustomAbstractions.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            CustomAbstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class NoopBehavior2<TRequest, TResponse> : CustomAbstractions.IPipelineBehavior<TRequest, TResponse>
        where TRequest : CustomAbstractions.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            CustomAbstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class NoopBehavior3<TRequest, TResponse> : CustomAbstractions.IPipelineBehavior<TRequest, TResponse>
        where TRequest : CustomAbstractions.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            CustomAbstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class NoopBehavior4<TRequest, TResponse> : CustomAbstractions.IPipelineBehavior<TRequest, TResponse>
        where TRequest : CustomAbstractions.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            CustomAbstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class NoopBehavior5<TRequest, TResponse> : CustomAbstractions.IPipelineBehavior<TRequest, TResponse>
        where TRequest : CustomAbstractions.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            CustomAbstractions.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class MediatRNoopBehavior1<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class MediatRNoopBehavior2<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class MediatRNoopBehavior3<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class MediatRNoopBehavior4<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class MediatRNoopBehavior5<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }
}
