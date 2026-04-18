using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Messaging.Extensions;
using CustomAbstractions = Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.PerformanceTests.Messaging;

[MemoryDiagnoser]
[RankColumn]
public class MessagingPolymorphicBenchmarks : IDisposable
{
    public enum WorkloadDistribution
    {
        Uniform,
        Hotset80_20
    }

    private static readonly Type[] MarkerTypes =
    [
        typeof(Marker001), typeof(Marker002), typeof(Marker003), typeof(Marker004), typeof(Marker005),
        typeof(Marker006), typeof(Marker007), typeof(Marker008), typeof(Marker009), typeof(Marker010),
        typeof(Marker011), typeof(Marker012), typeof(Marker013), typeof(Marker014), typeof(Marker015),
        typeof(Marker016), typeof(Marker017), typeof(Marker018), typeof(Marker019), typeof(Marker020),
        typeof(Marker021), typeof(Marker022), typeof(Marker023), typeof(Marker024), typeof(Marker025),
        typeof(Marker026), typeof(Marker027), typeof(Marker028), typeof(Marker029), typeof(Marker030),
        typeof(Marker031), typeof(Marker032), typeof(Marker033), typeof(Marker034), typeof(Marker035),
        typeof(Marker036), typeof(Marker037), typeof(Marker038), typeof(Marker039), typeof(Marker040),
        typeof(Marker041), typeof(Marker042), typeof(Marker043), typeof(Marker044), typeof(Marker045),
        typeof(Marker046), typeof(Marker047), typeof(Marker048), typeof(Marker049), typeof(Marker050),
        typeof(Marker051), typeof(Marker052), typeof(Marker053), typeof(Marker054), typeof(Marker055),
        typeof(Marker056), typeof(Marker057), typeof(Marker058), typeof(Marker059), typeof(Marker060),
        typeof(Marker061), typeof(Marker062), typeof(Marker063), typeof(Marker064), typeof(Marker065),
        typeof(Marker066), typeof(Marker067), typeof(Marker068), typeof(Marker069), typeof(Marker070),
        typeof(Marker071), typeof(Marker072), typeof(Marker073), typeof(Marker074), typeof(Marker075),
        typeof(Marker076), typeof(Marker077), typeof(Marker078), typeof(Marker079), typeof(Marker080),
        typeof(Marker081), typeof(Marker082), typeof(Marker083), typeof(Marker084), typeof(Marker085),
        typeof(Marker086), typeof(Marker087), typeof(Marker088), typeof(Marker089), typeof(Marker090),
        typeof(Marker091), typeof(Marker092), typeof(Marker093), typeof(Marker094), typeof(Marker095),
        typeof(Marker096), typeof(Marker097), typeof(Marker098), typeof(Marker099), typeof(Marker100)
    ];

    [Params(10, 50, 100)]
    public int RequestTypeCount { get; set; }

    [Params(WorkloadDistribution.Uniform, WorkloadDistribution.Hotset80_20)]
    public WorkloadDistribution Distribution { get; set; }

    private ServiceProvider _customProvider = default!;
    private ServiceProvider _mediatRProvider = default!;

    private CustomAbstractions.ISender _customSender = default!;
    private IMediator _mediatRMediator = default!;

    private object[] _customRequests = default!;
    private object[] _mediatRRequests = default!;
    private int _customIndex;
    private int _mediatRIndex;

    private bool _disposed;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _customProvider = BuildCustomProvider();
        _mediatRProvider = BuildMediatRProvider();

        _customSender = _customProvider.GetRequiredService<CustomAbstractions.ISender>();
        _mediatRMediator = _mediatRProvider.GetRequiredService<IMediator>();

        BuildRequestArrays();

        for (var i = 0; i < RequestTypeCount; i++)
        {
            _ = await _customSender.Send(_customRequests[i], CancellationToken.None).ConfigureAwait(false);
            _ = await _mediatRMediator.Send(_mediatRRequests[i], CancellationToken.None).ConfigureAwait(false);
        }
    }

    [Benchmark(Baseline = true)]
    public Task<object?> Custom_Send_Object_Polymorphic()
    {
        var index = GetNextIndex(ref _customIndex, _customRequests.Length);
        return _customSender.Send(_customRequests[index], CancellationToken.None);
    }

    [Benchmark]
    public Task<object?> MediatR_Send_Object_Polymorphic()
    {
        var index = GetNextIndex(ref _mediatRIndex, _mediatRRequests.Length);
        return _mediatRMediator.Send(_mediatRRequests[index], CancellationToken.None);
    }

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

    private static int GetNextIndex(ref int index, int length)
    {
        var current = index;
        index = current + 1;
        if (index == length)
            index = 0;

        return current;
    }

    private void BuildRequestArrays()
    {
        var sequenceLength = 4096;
        _customRequests = new object[sequenceLength];
        _mediatRRequests = new object[sequenceLength];

        uint state = 12345;
        var hotsetCount = Math.Max(1, RequestTypeCount / 5);

        for (var i = 0; i < sequenceLength; i++)
        {
            int typeIndex;
            if (Distribution == WorkloadDistribution.Uniform)
            {
                typeIndex = NextInt(ref state, RequestTypeCount);
            }
            else
            {
                var chooseHotset = NextInt(ref state, 10) < 8;
                typeIndex = chooseHotset
                    ? NextInt(ref state, hotsetCount)
                    : NextInt(ref state, RequestTypeCount - hotsetCount) + hotsetCount;
            }

            var markerType = MarkerTypes[typeIndex];

            _customRequests[i] = Activator.CreateInstance(
                typeof(CustomPolyRequest<>).MakeGenericType(markerType),
                i) ?? throw new InvalidOperationException("Failed to create custom polymorphic request instance.");

            _mediatRRequests[i] = Activator.CreateInstance(
                typeof(MediatRPolyRequest<>).MakeGenericType(markerType),
                i) ?? throw new InvalidOperationException("Failed to create MediatR polymorphic request instance.");
        }
    }

    private static int NextInt(ref uint state, int maxExclusive)
    {
        state = 1664525 * state + 1013904223;
        return (int)(state % (uint)maxExclusive);
    }

    private static ServiceProvider BuildCustomProvider()
    {
        var services = new ServiceCollection();

        services.AddMediator(configure => configure
            .AddOpenBehavior(typeof(CustomNoopBehavior<,>)));

        foreach (var markerType in MarkerTypes)
        {
            var requestType = typeof(CustomPolyRequest<>).MakeGenericType(markerType);
            var handlerType = typeof(CustomPolyRequestHandler<>).MakeGenericType(markerType);
            var serviceType = typeof(CustomAbstractions.IRequestHandler<,>).MakeGenericType(requestType, typeof(int));
            services.AddTransient(serviceType, handlerType);
        }

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildMediatRProvider()
    {
        var services = new ServiceCollection();

        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssemblies(typeof(IMediator).Assembly)
            .AddOpenBehavior(typeof(MediatRNoopBehavior<,>)));

        foreach (var markerType in MarkerTypes)
        {
            var requestType = typeof(MediatRPolyRequest<>).MakeGenericType(markerType);
            var handlerType = typeof(MediatRPolyRequestHandler<>).MakeGenericType(markerType);
            var serviceType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(int));
            services.AddTransient(serviceType, handlerType);
        }

        return services.BuildServiceProvider();
    }

#pragma warning disable S2326
    private sealed record CustomPolyRequest<TMarker>(int Value) : CustomAbstractions.IRequest<int>;

    private sealed class CustomPolyRequestHandler<TMarker> : CustomAbstractions.IRequestHandler<CustomPolyRequest<TMarker>, int>
    {
        public Task<int> Handle(CustomPolyRequest<TMarker> request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value + 1);
    }

    private sealed class CustomNoopBehavior<TRequest, TResponse> : CustomAbstractions.IRequestPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            CustomAbstractions.RequestExecutionDelegate<TRequest, TResponse> next,
            CancellationToken cancellationToken)
            => next(request, cancellationToken);
    }

    private sealed record MediatRPolyRequest<TMarker>(int Value) : IRequest<int>;
#pragma warning restore S2326

    private sealed class MediatRPolyRequestHandler<TMarker> : IRequestHandler<MediatRPolyRequest<TMarker>, int>
    {
        public Task<int> Handle(MediatRPolyRequest<TMarker> request, CancellationToken cancellationToken)
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

#pragma warning disable S101
    private interface Marker001; private interface Marker002; private interface Marker003; private interface Marker004; private interface Marker005;
    private interface Marker006; private interface Marker007; private interface Marker008; private interface Marker009; private interface Marker010;
    private interface Marker011; private interface Marker012; private interface Marker013; private interface Marker014; private interface Marker015;
    private interface Marker016; private interface Marker017; private interface Marker018; private interface Marker019; private interface Marker020;
    private interface Marker021; private interface Marker022; private interface Marker023; private interface Marker024; private interface Marker025;
    private interface Marker026; private interface Marker027; private interface Marker028; private interface Marker029; private interface Marker030;
    private interface Marker031; private interface Marker032; private interface Marker033; private interface Marker034; private interface Marker035;
    private interface Marker036; private interface Marker037; private interface Marker038; private interface Marker039; private interface Marker040;
    private interface Marker041; private interface Marker042; private interface Marker043; private interface Marker044; private interface Marker045;
    private interface Marker046; private interface Marker047; private interface Marker048; private interface Marker049; private interface Marker050;
    private interface Marker051; private interface Marker052; private interface Marker053; private interface Marker054; private interface Marker055;
    private interface Marker056; private interface Marker057; private interface Marker058; private interface Marker059; private interface Marker060;
    private interface Marker061; private interface Marker062; private interface Marker063; private interface Marker064; private interface Marker065;
    private interface Marker066; private interface Marker067; private interface Marker068; private interface Marker069; private interface Marker070;
    private interface Marker071; private interface Marker072; private interface Marker073; private interface Marker074; private interface Marker075;
    private interface Marker076; private interface Marker077; private interface Marker078; private interface Marker079; private interface Marker080;
    private interface Marker081; private interface Marker082; private interface Marker083; private interface Marker084; private interface Marker085;
    private interface Marker086; private interface Marker087; private interface Marker088; private interface Marker089; private interface Marker090;
    private interface Marker091; private interface Marker092; private interface Marker093; private interface Marker094; private interface Marker095;
    private interface Marker096; private interface Marker097; private interface Marker098; private interface Marker099; private interface Marker100;
#pragma warning restore S101
}
