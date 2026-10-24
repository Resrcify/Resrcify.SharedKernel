using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Bus.Advanced;
using Rebus.Config;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Lifetime;
using Resrcify.SharedKernel.MessageBus.UnitTests.Hosting;
using Resrcify.SharedKernel.MessageBus.UnitTests.Strategies;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Extensions;

/// <summary>
/// The service's bus (one-way, or with an input queue) is disposed once when a test host stops its host twice at once
/// and its container disposes it too, and no stop returns while the bus is still being disposed. The first stop's
/// disposal is held (<see cref="HeldBusStrategy"/>, at Rebus' <c>BusDisposing</c>) until the other callers are where
/// the test needs them: waiting for it, or (without the fix) done already. A probe bus that the configuration strategy
/// puts around Rebus' bus counts the disposals that get past the service's bus. Nothing waits on time.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusServiceCollectionExtensionsBusDisposalTests
{
    /// <summary>The scenario runs this often, each on a fresh host.</summary>
    private const int Rounds = 5;

    /// <summary>A bound for waits that only a failed test reaches.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddMessageBus_ShouldGiveTheHostsStopsAndTheContainerTheDisposeOnceBus_AroundTheStrategysOwn(bool oneWay)
    {
        var queue = oneWay ? null : $"disposed-once-{Guid.NewGuid():N}";
        using var held = new HeldBusStrategy(address => address == queue);
        var strategy = new ProbeStrategy(held);
        using var logs = new DisposalWaitLogs();
        using var host = await StartAsync(queue, strategy, logs);

        var stopped = StoppedBus(host);
        var disposed = DisposedBuses(host);

        stopped.ShouldBeOfType<DisposeOnceBus>();
        disposed.ShouldHaveSingleItem().ShouldBeSameAs(stopped);
        strategy.Probes.ShouldHaveSingleItem().Inner.ShouldNotBeOfType<DisposeOnceBus>();
        await host.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAsync_ShouldDisposeTheBusOnce_AndReturnOnlyOnceItIsDisposed_WhenTheHostIsStoppedTwiceAtOnceAndDisposed(bool oneWay)
    {
        for (var round = 0; round < Rounds; round++)
            await StopTwiceAndDisposeAtOnceAsync(oneWay, round);
    }

    /// <summary>
    /// What a test host does: <c>WebApplicationFactory.DisposeAsync</c> stops the host, which wakes <c>Program.cs</c>'s
    /// <c>app.RunAsync()</c>, and that stops it too; then the container is disposed. Here the first stop holds the
    /// bus' disposal while the second stop and the container's disposal of the bus (Rebus.ServiceProvider's disposal
    /// helper) arrive.
    /// </summary>
    private static async Task StopTwiceAndDisposeAtOnceAsync(
        bool oneWay,
        int round)
    {
        var queue = oneWay ? null : $"stopped-twice-{Guid.NewGuid():N}";
        using var held = new HeldBusStrategy(address => address == queue);
        var strategy = new ProbeStrategy(held);
        using var logs = new DisposalWaitLogs();
        using var host = await StartAsync(queue, strategy, logs);
        var disposing = 0;
        host.Services.GetRequiredService<BusLifetimeEvents>().BusDisposing += () => Interlocked.Increment(ref disposing);
        var disposalHelper = DisposalHelper(host);
        held.HoldNextDisposal();

        // The factory's stop, on a thread of its own: holding the bus' disposal holds its caller's thread.
        var factoryStop = Task.Run(() => host.StopAsync());
        Task programStop;
        Task containerDisposal;
        try
        {
            await held.DisposalHeld.WaitAsync(GiveUpAfter);
            programStop = Task.Run(() => host.StopAsync());
            containerDisposal = Task.Run(disposalHelper.Dispose);

            // Both wait for the held disposal, or (without the fix) have returned already.
            await Task.WhenAny(Task.WhenAll(programStop, containerDisposal), logs.WaitsReachedAsync(2)).WaitAsync(GiveUpAfter);

            strategy.Probes.ShouldHaveSingleItem().Disposals.ShouldBe(
                1,
                $"round {round}: the bus inside was disposed by more than one caller at once");
            programStop.IsCompleted.ShouldBeFalse($"round {round}: the second stop returned while the bus was being disposed");
            containerDisposal.IsCompleted.ShouldBeFalse($"round {round}: the container's disposal returned while the bus was being disposed");
        }
        finally
        {
            held.ReleaseDisposal();
            await factoryStop.WaitAsync(GiveUpAfter);
        }

        await programStop.WaitAsync(GiveUpAfter);
        await containerDisposal.WaitAsync(GiveUpAfter);
        host.Dispose();

        Volatile.Read(ref disposing).ShouldBe(1, $"round {round}");
        held.Disposed.ShouldBe(1, $"round {round}");
        strategy.Probes.ShouldHaveSingleItem().Disposals.ShouldBe(1, $"round {round}");
        logs.Waits.ShouldBe(2, $"round {round}");
    }

    private static Task<IHost> StartAsync(
        string? queue,
        ProbeStrategy strategy,
        DisposalWaitLogs logs)
        => InMemoryServices.StartAsync(
            new InMemNetwork(),
            bus => (queue is null ? bus : bus.WithInputQueue(queue)).UseConfigurationStrategy(strategy),
            services => services.AddLogging(logging => logging
                .AddProvider(logs)
                .AddFilter(typeof(DisposeOnceBus).FullName, LogLevel.Debug)));

    /// <summary>The bus Rebus.ServiceProvider's hosted service disposes on each of the host's <c>StopAsync</c>.</summary>
    private static IBus StoppedBus(IHost host)
    {
        var service = host.Services.GetServices<IHostedService>()
            .Single(hosted => hosted.GetType().Name == "RebusBackgroundService");
        var field = service.GetType().GetField("_bus", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RebusBackgroundService no longer holds its bus in _bus.");
        return field.GetValue(service).ShouldBeAssignableTo<IBus>().ShouldNotBeNull();
    }

    /// <summary>The buses Rebus.ServiceProvider's disposal helper disposes when the container is disposed.</summary>
    private static IDisposable[] DisposedBuses(IHost host)
    {
        var helper = DisposalHelper(host);
        var field = helper.GetType().GetField("_disposables", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RebusDisposalHelper no longer holds its buses in _disposables.");
        return [.. field.GetValue(helper).ShouldBeOfType<ConcurrentStack<IDisposable>>()];
    }

    private static IDisposable DisposalHelper(IHost host)
    {
        var type = typeof(Rebus.Config.ServiceCollectionExtensions).Assembly
            .GetType("Rebus.ServiceProvider.Internals.RebusDisposalHelper", throwOnError: true)
            .ShouldNotBeNull();
        return host.Services.GetRequiredService(type).ShouldBeAssignableTo<IDisposable>().ShouldNotBeNull();
    }

    /// <summary>
    /// The held disposal's strategy, plus a probe around Rebus' bus, put there as any configuration strategy can
    /// (<c>options.Decorate&lt;IBus&gt;</c>).
    /// </summary>
    private sealed class ProbeStrategy(HeldBusStrategy held) : IBusConfigurationStrategy
    {
        private readonly ConcurrentQueue<DisposalProbe> _probes = new();

        public IReadOnlyCollection<DisposalProbe> Probes => _probes;

        public void ConfigureTransport(RabbitMqOptionsBuilder transport)
            => held.ConfigureTransport(transport);

        public void ConfigureOptions(OptionsConfigurer options)
        {
            held.ConfigureOptions(options);
            options.Decorate<IBus>(context =>
            {
                var probe = new DisposalProbe(context.Get<IBus>());
                _probes.Enqueue(probe);
                return probe;
            });
        }
    }

    /// <summary>
    /// Counts the disposals that reach it: every call but one from inside its own disposal, on that thread (Rebus
    /// disposes what it resolved, this probe included, from inside the bus' disposal).
    /// </summary>
    private sealed class DisposalProbe(IBus inner) : IBus
    {
        private readonly ConcurrentDictionary<int, bool> _disposingThreads = new();
        private int _disposals;

        public IBus Inner => inner;

        public int Disposals => Volatile.Read(ref _disposals);

        public IAdvancedApi Advanced => inner.Advanced;

        public void Dispose()
        {
            var thread = Environment.CurrentManagedThreadId;
            if (!_disposingThreads.TryAdd(thread, true))
                return;

            Interlocked.Increment(ref _disposals);
            try
            {
                inner.Dispose();
            }
            finally
            {
                _disposingThreads.TryRemove(thread, out _);
            }
        }

        public Task SendLocal(object commandMessage, IDictionary<string, string>? optionalHeaders = null)
            => inner.SendLocal(commandMessage, optionalHeaders);

        public Task Send(object commandMessage, IDictionary<string, string>? optionalHeaders = null)
            => inner.Send(commandMessage, optionalHeaders);

        public Task DeferLocal(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null)
            => inner.DeferLocal(delay, message, optionalHeaders);

        public Task Defer(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null)
            => inner.Defer(delay, message, optionalHeaders);

        public Task Reply(object replyMessage, IDictionary<string, string>? optionalHeaders = null)
            => inner.Reply(replyMessage, optionalHeaders);

        public Task Subscribe<TEvent>()
            => inner.Subscribe<TEvent>();

        public Task Subscribe(Type eventType)
            => inner.Subscribe(eventType);

        public Task Unsubscribe<TEvent>()
            => inner.Unsubscribe<TEvent>();

        public Task Unsubscribe(Type eventType)
            => inner.Unsubscribe(eventType);

        public Task Publish(object eventMessage, IDictionary<string, string>? optionalHeaders = null)
            => inner.Publish(eventMessage, optionalHeaders);
    }

    /// <summary>Counts the service's bus' "waiting until it is" lines: each is a caller waiting for another's disposal.</summary>
    private sealed class DisposalWaitLogs : ILoggerProvider
    {
        private readonly Lock _gate = new();
        private readonly List<(int Count, TaskCompletionSource Reached)> _signals = [];
        private int _waits;

        public int Waits
        {
            get
            {
                lock (_gate)
                    return _waits;
            }
        }

        public Task WaitsReachedAsync(int count)
        {
            lock (_gate)
            {
                if (_waits >= count)
                    return Task.CompletedTask;

                var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _signals.Add((count, reached));
                return reached.Task;
            }
        }

        public ILogger CreateLogger(string categoryName)
            => categoryName == typeof(DisposeOnceBus).FullName ? new WaitLogger(this) : NoLogger.Instance;

        public void Dispose()
        {
        }

        private void Waited()
        {
            List<TaskCompletionSource> reached;
            lock (_gate)
            {
                _waits++;
                reached = [.. _signals.Where(signal => signal.Count <= _waits).Select(signal => signal.Reached)];
                _signals.RemoveAll(signal => signal.Count <= _waits);
            }

            foreach (var signal in reached)
                signal.TrySetResult();
        }

        private sealed class WaitLogger(DisposalWaitLogs logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel)
                => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter(state, exception).EndsWith("waiting until it is", StringComparison.Ordinal))
                    logs.Waited();
            }
        }

        private sealed class NoLogger : ILogger
        {
            public static readonly NoLogger Instance = new();

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel)
                => false;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
