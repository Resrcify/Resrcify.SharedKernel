using System;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Strategies;

/// <summary>
/// Counts the buses built and disposed for the queues <paramref name="appliesTo"/> picks, and holds one start or one
/// disposal where the test says, until it lets it go: the start before the bus runs (its transport is being set up), the
/// disposal as it begins (Rebus' <c>BusDisposing</c>, on the thread disposing it). So a test puts a second caller exactly
/// where the first one is, with no timing involved. Every other bus starts and stops as usual.
/// </summary>
internal sealed class HeldBusStrategy(Func<string?, bool> appliesTo)
    : IBusConfigurationStrategy,
    IDisposable
{
    /// <summary>The longest a held start or disposal waits for the test: only reached when the test failed already.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _startHeld = NewSignal();
    private readonly TaskCompletionSource _disposalHeld = NewSignal();
    private readonly ManualResetEventSlim _startReleased = new();
    private readonly ManualResetEventSlim _disposalReleased = new();
    private int _holdStart;
    private int _holdDisposal;
    private int _built;
    private int _disposed;

    /// <summary>How many buses were built (each start that got as far as its transport).</summary>
    public int Built => Volatile.Read(ref _built);

    /// <summary>How many buses were disposed.</summary>
    public int Disposed => Volatile.Read(ref _disposed);

    /// <summary>Completes once the held start is held.</summary>
    public Task StartHeld => _startHeld.Task;

    /// <summary>Completes once the held disposal is held.</summary>
    public Task DisposalHeld => _disposalHeld.Task;

    /// <summary>Holds the next start, until <see cref="ReleaseStart"/>.</summary>
    public void HoldNextStart()
        => Volatile.Write(ref _holdStart, 1);

    /// <summary>Holds the next disposal, until <see cref="ReleaseDisposal"/>.</summary>
    public void HoldNextDisposal()
        => Volatile.Write(ref _holdDisposal, 1);

    public void ReleaseStart()
        => _startReleased.Set();

    public void ReleaseDisposal()
        => _disposalReleased.Set();

    public void ConfigureTransport(RabbitMqOptionsBuilder transport)
    {
    }

    public void ConfigureOptions(OptionsConfigurer options)
        => options.Decorate<ITransport>(context =>
        {
            var transport = context.Get<ITransport>();
            if (!appliesTo(transport.Address))
                return transport;

            var lifetime = context.Get<BusLifetimeEvents>();
            lifetime.BusDisposing += OnDisposing;
            lifetime.BusDisposed += () => Interlocked.Increment(ref _disposed);
            Interlocked.Increment(ref _built);
            if (Interlocked.Exchange(ref _holdStart, 0) == 1)
                Hold(_startHeld, _startReleased);
            return transport;
        });

    public void Dispose()
    {
        // Nothing stays held once the test is over.
        _startReleased.Set();
        _disposalReleased.Set();
        _startReleased.Dispose();
        _disposalReleased.Dispose();
    }

    private void OnDisposing()
    {
        if (Interlocked.Exchange(ref _holdDisposal, 0) == 1)
            Hold(_disposalHeld, _disposalReleased);
    }

    private static void Hold(
        TaskCompletionSource held,
        ManualResetEventSlim released)
    {
        held.TrySetResult();
        released.Wait(GiveUpAfter);
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
