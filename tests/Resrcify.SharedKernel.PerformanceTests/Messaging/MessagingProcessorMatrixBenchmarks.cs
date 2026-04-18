using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Extensions;

namespace Resrcify.SharedKernel.PerformanceTests.Messaging;

[MemoryDiagnoser]
public class MessagingProcessorMatrixBenchmarks : IDisposable
{
    [Params(0, 1, 3)]
    public int PreProcessorCount { get; set; }

    [Params(0, 1, 3)]
    public int PostProcessorCount { get; set; }

    private ServiceProvider _provider = default!;
    private ISender _sender = default!;
    private ProcessorRequest _request = new(13);

    private bool _disposed;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _request = new ProcessorRequest(13);
        _provider = BuildProvider();
        _sender = _provider.GetRequiredService<ISender>();

        _ = await _sender.Send(_request, CancellationToken.None).ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public Task<int> Custom_Send_With_PrePost_Matrix()
        => _sender.Send(_request, CancellationToken.None);

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

        services.AddTransient<IRequestHandler<ProcessorRequest, int>, ProcessorRequestHandler>();

        if (PreProcessorCount >= 1)
            services.AddTransient<IRequestPreProcessor<ProcessorRequest>, PreProcessorOne>();

        if (PreProcessorCount >= 2)
            services.AddTransient<IRequestPreProcessor<ProcessorRequest>, PreProcessorTwo>();

        if (PreProcessorCount >= 3)
            services.AddTransient<IRequestPreProcessor<ProcessorRequest>, PreProcessorThree>();

        if (PostProcessorCount >= 1)
            services.AddTransient<IRequestPostProcessor<ProcessorRequest, int>, PostProcessorOne>();

        if (PostProcessorCount >= 2)
            services.AddTransient<IRequestPostProcessor<ProcessorRequest, int>, PostProcessorTwo>();

        if (PostProcessorCount >= 3)
            services.AddTransient<IRequestPostProcessor<ProcessorRequest, int>, PostProcessorThree>();

        return services.BuildServiceProvider();
    }

    private sealed record ProcessorRequest(int Value) : IRequest<int>;

    private sealed class ProcessorRequestHandler : IRequestHandler<ProcessorRequest, int>
    {
        public Task<int> Handle(ProcessorRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value + 1);
    }

    private sealed class PreProcessorOne : IRequestPreProcessor<ProcessorRequest>
    {
        public Task Process(ProcessorRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class PreProcessorTwo : IRequestPreProcessor<ProcessorRequest>
    {
        public Task Process(ProcessorRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class PreProcessorThree : IRequestPreProcessor<ProcessorRequest>
    {
        public Task Process(ProcessorRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class PostProcessorOne : IRequestPostProcessor<ProcessorRequest, int>
    {
        public Task Process(ProcessorRequest request, int response, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class PostProcessorTwo : IRequestPostProcessor<ProcessorRequest, int>
    {
        public Task Process(ProcessorRequest request, int response, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class PostProcessorThree : IRequestPostProcessor<ProcessorRequest, int>
    {
        public Task Process(ProcessorRequest request, int response, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
