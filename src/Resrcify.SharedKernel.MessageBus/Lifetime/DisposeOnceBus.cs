using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Bus;
using Rebus.Bus.Advanced;
using Rebus.Logging;

namespace Resrcify.SharedKernel.MessageBus.Lifetime;

/// <summary>
/// The service's bus, disposed once however many callers dispose it at once: the outermost <see cref="IBus"/> of the
/// bus <c>AddMessageBus</c> builds, so it is the one Rebus.ServiceProvider's hosted service disposes on each
/// <c>StopAsync</c>, and its disposal helper when the container is disposed. Every other member goes to the bus inside.
/// </summary>
/// <remarks>
/// <para>
/// A test host stops its host twice at once (<c>WebApplicationFactory.DisposeAsync</c> stops it, which wakes
/// <c>Program.cs</c>'s <c>app.RunAsync()</c>, and that stops it too), so the bus is disposed on two threads at once.
/// Rebus' own guards against a second disposal (in <c>RebusBus</c>, its error tracker, its periodic tasks) check a
/// flag and then set it, so two callers can both pass them, and the second one then cancels a cancellation source the
/// first has disposed (<see cref="ObjectDisposedException"/>).
/// </para>
/// <para>
/// Here the first caller disposes the bus inside under a lock. A caller that arrives meanwhile waits for that
/// disposal to end, then returns: no caller returns while the bus is still being disposed. A caller that comes back
/// from inside the disposal, on the thread disposing it (Rebus disposes the instances it resolved, this one included,
/// when the bus inside is disposed), returns at once. A disposal that throws throws to its caller only; the bus counts
/// as disposed.
/// </para>
/// </remarks>
internal sealed class DisposeOnceBus(
    IBus inner,
    ILog log)
    : IBus
{
    private readonly IBus _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ILog _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly Lock _gate = new();

    // Written under the gate; read without it only to skip the gate once the bus is disposed.
    private volatile bool _disposed;

    // Under the gate: the disposal is under way, on the thread that holds the gate.
    private bool _disposing;

    public IAdvancedApi Advanced
        => _inner.Advanced;

    public void Dispose()
    {
        if (_disposed)
            return;

        if (!_gate.TryEnter())
        {
            _log.Debug("Bus {busName} is being disposed by another caller: waiting until it is", _inner);
            _gate.Enter();
        }

        try
        {
            // Disposed already, or this thread's own disposal calling back from inside the bus.
            if (_disposed || _disposing)
                return;

            _disposing = true;
            DisposeInner();
        }
        finally
        {
            _gate.Exit();
        }
    }

    public Task SendLocal(
        object commandMessage,
        IDictionary<string, string>? optionalHeaders = null)
        => _inner.SendLocal(commandMessage, optionalHeaders);

    public Task Send(
        object commandMessage,
        IDictionary<string, string>? optionalHeaders = null)
        => _inner.Send(commandMessage, optionalHeaders);

    public Task DeferLocal(
        TimeSpan delay,
        object message,
        IDictionary<string, string>? optionalHeaders = null)
        => _inner.DeferLocal(delay, message, optionalHeaders);

    public Task Defer(
        TimeSpan delay,
        object message,
        IDictionary<string, string>? optionalHeaders = null)
        => _inner.Defer(delay, message, optionalHeaders);

    public Task Reply(
        object replyMessage,
        IDictionary<string, string>? optionalHeaders = null)
        => _inner.Reply(replyMessage, optionalHeaders);

    public Task Subscribe<TEvent>()
        => _inner.Subscribe<TEvent>();

    public Task Subscribe(Type eventType)
        => _inner.Subscribe(eventType);

    public Task Unsubscribe<TEvent>()
        => _inner.Unsubscribe<TEvent>();

    public Task Unsubscribe(Type eventType)
        => _inner.Unsubscribe(eventType);

    public Task Publish(
        object eventMessage,
        IDictionary<string, string>? optionalHeaders = null)
        => _inner.Publish(eventMessage, optionalHeaders);

    /// <summary>The bus inside's name (Rebus.ServiceProvider logs the bus by it).</summary>
    public override string? ToString()
        => _inner.ToString();

    private void DisposeInner()
    {
        try
        {
            _inner.Dispose();
        }
        finally
        {
            _disposed = true;
        }
    }
}
