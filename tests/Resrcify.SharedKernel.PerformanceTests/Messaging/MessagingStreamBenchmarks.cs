using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Extensions;

namespace Resrcify.SharedKernel.PerformanceTests.Messaging;

[MemoryDiagnoser]
public class MessagingStreamBenchmarks : IDisposable
{
    [Params(0, 1, 3)]
    public int BehaviorCount { get; set; }

    [Params(1, 16)]
    public int ItemCount { get; set; }

    private ServiceProvider _provider = default!;
    private IStreamSender _streamSender = default!;
    private StreamRequest _request = default!;

    private bool _disposed;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _request = new StreamRequest(1, ItemCount);
        _provider = BuildProvider();
        _streamSender = _provider.GetRequiredService<IStreamSender>();

        _ = await ConsumeAsync(_streamSender.CreateStream(_request, CancellationToken.None)).ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public Task<int> Custom_CreateStream_ConsumeAll()
        => ConsumeAsync(_streamSender.CreateStream(_request, CancellationToken.None));

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

        services.AddMediator(_ => { });
        services.AddTransient<IStreamRequestHandler<StreamRequest, int>, StreamRequestHandler>();

        if (BehaviorCount >= 1)
            services.AddTransient<IStreamPipelineBehavior<StreamRequest, int>, StreamNoopBehaviorOne>();

        if (BehaviorCount >= 2)
            services.AddTransient<IStreamPipelineBehavior<StreamRequest, int>, StreamNoopBehaviorTwo>();

        if (BehaviorCount >= 3)
            services.AddTransient<IStreamPipelineBehavior<StreamRequest, int>, StreamNoopBehaviorThree>();

        return services.BuildServiceProvider();
    }

    private static async Task<int> ConsumeAsync(IAsyncEnumerable<int> stream)
    {
        var sum = 0;
        await foreach (var item in stream.ConfigureAwait(false))
            sum += item;

        return sum;
    }

    private sealed record StreamRequest(int Start, int Count) : IStreamRequest<int>;

    private sealed class StreamRequestHandler : IStreamRequestHandler<StreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(
            StreamRequest request,
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

    private sealed class StreamNoopBehaviorOne : IStreamPipelineBehavior<StreamRequest, int>
    {
        public IAsyncEnumerable<int> Handle(
            StreamRequest request,
            StreamHandlerDelegate<int> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class StreamNoopBehaviorTwo : IStreamPipelineBehavior<StreamRequest, int>
    {
        public IAsyncEnumerable<int> Handle(
            StreamRequest request,
            StreamHandlerDelegate<int> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class StreamNoopBehaviorThree : IStreamPipelineBehavior<StreamRequest, int>
    {
        public IAsyncEnumerable<int> Handle(
            StreamRequest request,
            StreamHandlerDelegate<int> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }
}
