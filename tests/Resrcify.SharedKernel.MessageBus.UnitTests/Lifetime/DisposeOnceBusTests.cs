using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Bus;
using Rebus.Bus.Advanced;
using Rebus.Logging;
using Resrcify.SharedKernel.MessageBus.Lifetime;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Lifetime;

/// <summary>
/// The service's bus is disposed once, however many callers dispose it at once, and no caller returns before that
/// disposal ended. The bus inside is a stand-in that holds its disposal until the test lets it go, so the test puts the
/// callers where it needs them (one inside the disposal, the others waiting for it) and only then lets it end: nothing
/// waits on time. The callers start together (a barrier), round after round.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DisposeOnceBusTests
{
    private const int Rounds = 200;
    private const int Callers = 8;

    /// <summary>A bound for waits that only a failed test reaches.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    [Fact]
    public void Dispose_ShouldDisposeTheBusInsideOnce_AndReturnToEveryCallerOnlyAfterThatDisposalEnded_WhenManyDisposeAtOnce()
    {
        var progress = new SemaphoreSlim(0);
        var inners = Enumerable.Range(0, Rounds).Select(_ => new HeldBus(progress)).ToArray();
        var logs = Enumerable.Range(0, Rounds).Select(_ => new WaitingLog(progress)).ToArray();
        var buses = Enumerable.Range(0, Rounds).Select(round => new DisposeOnceBus(inners[round], logs[round])).ToArray();
        var returnedAfterTheDisposalEnded = Enumerable.Range(0, Rounds).Select(_ => new bool[Callers]).ToArray();
        using (progress)
        using (var roundStarts = new Barrier(Callers + 1))
        using (var roundEnds = new Barrier(Callers + 1))
        {
            var failures = new Exception?[Callers];
            var threads = Enumerable.Range(0, Callers)
                .Select(caller => new Thread(() =>
                {
                    for (var round = 0; round < Rounds; round++)
                    {
                        roundStarts.SignalAndWait(GiveUpAfter);
                        try
                        {
                            buses[round].Dispose();
                            returnedAfterTheDisposalEnded[round][caller] = inners[round].DisposalEnded;
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            failures[caller] ??= exception;
                        }

                        roundEnds.SignalAndWait(GiveUpAfter);
                    }
                }))
                .ToArray();
            foreach (var thread in threads)
                thread.Start();

            for (var round = 0; round < Rounds; round++)
            {
                roundStarts.SignalAndWait(GiveUpAfter).ShouldBeTrue($"round {round}: the callers didn't start");
                // Let the disposal end only once one caller is inside it and every other one waits for it, or a second
                // caller got inside (the failure this test is for).
                WaitUntil(
                    progress,
                    () => inners[round].Disposals >= 2 || inners[round].Disposals == 1 && logs[round].Waits == Callers - 1,
                    $"round {round}");
                inners[round].Release();
                roundEnds.SignalAndWait(GiveUpAfter).ShouldBeTrue($"round {round}: a caller didn't return");
            }

            foreach (var thread in threads)
                thread.Join();
            failures.OfType<Exception>().ShouldBeEmpty();
        }

        for (var round = 0; round < Rounds; round++)
        {
            inners[round].Disposals.ShouldBe(1, $"round {round}: the bus inside was disposed {inners[round].Disposals} times");
            logs[round].Waits.ShouldBe(Callers - 1, $"round {round}");
            returnedAfterTheDisposalEnded[round].ShouldAllBe(
                returned => returned,
                $"round {round}: a caller returned while the bus was still being disposed");
        }
    }

    [Fact]
    public async Task Dispose_ShouldReturnAtOnce_WhenTheDisposalCallsItBackOnItsOwnThread()
    {
        // Rebus disposes the instances it resolved, this bus included, from inside the bus' own disposal.
        using var inner = new HeldBus(progress: null) { HoldsDisposal = false };
        using var bus = new DisposeOnceBus(inner, new WaitingLog(progress: null));
        inner.OnDispose = bus.Dispose;

        // On a thread of its own, so a disposal that waits for itself fails the test instead of hanging it.
        await Task.Run(bus.Dispose).WaitAsync(GiveUpAfter);

        inner.Disposals.ShouldBe(1);
        inner.CallsBack.ShouldBe(0, "the call back reached the bus inside");
        inner.DisposalEnded.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ShouldNotDisposeTheBusInsideAgain_WhenItWasDisposedAlready()
    {
        using var inner = new HeldBus(progress: null) { HoldsDisposal = false };
        var log = new WaitingLog(progress: null);
        using var bus = new DisposeOnceBus(inner, log);

        bus.Dispose();
        bus.Dispose();

        inner.Disposals.ShouldBe(1);
        log.Waits.ShouldBe(0);
    }

    [Fact]
    public void Dispose_ShouldThrowToItsCallerOnly_AndCountTheBusAsDisposed_WhenTheDisposalThrows()
    {
        var failure = new InvalidOperationException("A worker didn't stop.");
        var throws = true;
        // Only its first disposal throws: the test's own disposal of it, at the end, doesn't.
        using var inner = new HeldBus(progress: null)
        {
            HoldsDisposal = false,
            OnDispose = () =>
            {
                if (!throws)
                    return;
                throws = false;
                throw failure;
            },
        };
        using var bus = new DisposeOnceBus(inner, new WaitingLog(progress: null));

        Should.Throw<InvalidOperationException>(bus.Dispose).ShouldBeSameAs(failure);
        bus.Dispose();

        inner.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task EveryOtherMember_ShouldGoToTheBusInside()
    {
        using var inner = new HeldBus(progress: null) { HoldsDisposal = false };
        using var bus = new DisposeOnceBus(inner, new WaitingLog(progress: null));
        var message = new object();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["h"] = "v" };
        var delay = TimeSpan.FromSeconds(3);
        var eventType = typeof(int);

        await bus.SendLocal(message, headers);
        await bus.Send(message, headers);
        await bus.DeferLocal(delay, message, headers);
        await bus.Defer(delay, message, headers);
        await bus.Reply(message, headers);
        await bus.Subscribe<string>();
        await bus.Subscribe(eventType);
        await bus.Unsubscribe<string>();
        await bus.Unsubscribe(eventType);
        await bus.Publish(message, headers);

        inner.Calls.ShouldBe([
            "SendLocal", "Send", "DeferLocal 00:00:03", "Defer 00:00:03", "Reply", "Subscribe String", "Subscribe Int32",
            "Unsubscribe String", "Unsubscribe Int32", "Publish"]);
        inner.Messages.ShouldAllBe(sent => ReferenceEquals(sent, message));
        inner.Headers.ShouldAllBe(sent => ReferenceEquals(sent, headers));
        bus.Advanced.ShouldBeSameAs(inner.Advanced);
        bus.ToString().ShouldBe(inner.ToString());
        inner.Disposals.ShouldBe(0);
    }

    /// <summary>Waits, each time <paramref name="progress"/> is signalled, until <paramref name="condition"/> holds.</summary>
    private static void WaitUntil(
        SemaphoreSlim progress,
        Func<bool> condition,
        string what)
    {
        while (!condition())
            progress.Wait(GiveUpAfter).ShouldBeTrue($"{what}: nothing happened");
    }

    /// <summary>
    /// The bus inside: counts its disposals and holds each until <see cref="Release"/> (unless it doesn't hold), records
    /// every other call, and runs <see cref="OnDispose"/> from inside its disposal.
    /// </summary>
    private sealed class HeldBus(SemaphoreSlim? progress) : IBus
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposals;
        private int _callsBack;
        private int _disposingThread;
        private volatile bool _disposalEnded;

        public bool HoldsDisposal { get; init; } = true;

        public Action? OnDispose { get; set; }

        public int Disposals => Volatile.Read(ref _disposals);

        /// <summary>How many calls came from inside a disposal, on its thread.</summary>
        public int CallsBack => Volatile.Read(ref _callsBack);

        /// <summary>Whether a disposal ran to its end.</summary>
        public bool DisposalEnded => _disposalEnded;

        public List<string> Calls { get; } = [];

        public List<object> Messages { get; } = [];

        public List<IDictionary<string, string>?> Headers { get; } = [];

        public IAdvancedApi Advanced { get; } = new NoAdvancedApi();

        public void Release()
            => _released.TrySetResult();

        public void Dispose()
        {
            // A call from inside its own disposal, as Rebus makes; any other call is a disposal of its own (Rebus'
            // guards don't stop two callers at once, so none here does).
            if (Volatile.Read(ref _disposingThread) == Environment.CurrentManagedThreadId)
            {
                Interlocked.Increment(ref _callsBack);
                return;
            }

            Interlocked.Increment(ref _disposals);
            Volatile.Write(ref _disposingThread, Environment.CurrentManagedThreadId);
            try
            {
                progress?.Release();
                if (HoldsDisposal)
                    _released.Task.Wait(GiveUpAfter).ShouldBeTrue("the test never let the disposal end");
                OnDispose?.Invoke();
                _disposalEnded = true;
            }
            finally
            {
                Volatile.Write(ref _disposingThread, 0);
            }
        }

        public Task SendLocal(object commandMessage, IDictionary<string, string>? optionalHeaders = null)
            => Record("SendLocal", commandMessage, optionalHeaders);

        public Task Send(object commandMessage, IDictionary<string, string>? optionalHeaders = null)
            => Record("Send", commandMessage, optionalHeaders);

        public Task DeferLocal(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null)
            => Record($"DeferLocal {delay}", message, optionalHeaders);

        public Task Defer(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null)
            => Record($"Defer {delay}", message, optionalHeaders);

        public Task Reply(object replyMessage, IDictionary<string, string>? optionalHeaders = null)
            => Record("Reply", replyMessage, optionalHeaders);

        public Task Subscribe<TEvent>()
            => Record($"Subscribe {typeof(TEvent).Name}");

        public Task Subscribe(Type eventType)
            => Record($"Subscribe {eventType.Name}");

        public Task Unsubscribe<TEvent>()
            => Record($"Unsubscribe {typeof(TEvent).Name}");

        public Task Unsubscribe(Type eventType)
            => Record($"Unsubscribe {eventType.Name}");

        public Task Publish(object eventMessage, IDictionary<string, string>? optionalHeaders = null)
            => Record("Publish", eventMessage, optionalHeaders);

        public override string ToString()
            => "held bus";

        private Task Record(
            string call,
            object? message = null,
            IDictionary<string, string>? headers = null)
        {
            Calls.Add(call);
            if (message is not null)
            {
                Messages.Add(message);
                Headers.Add(headers);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Counts the callers that waited for another's disposal (the one line the bus logs).</summary>
    private sealed class WaitingLog(SemaphoreSlim? progress) : ILog
    {
        private int _waits;

        public int Waits => Volatile.Read(ref _waits);

        public void Debug(string message, params object[] objs)
        {
            Interlocked.Increment(ref _waits);
            progress?.Release();
        }

        public void Info(string message, params object[] objs)
            => throw new NotSupportedException(message);

        public void Warn(string message, params object[] objs)
            => throw new NotSupportedException(message);

        public void Warn(Exception exception, string message, params object[] objs)
            => throw new NotSupportedException(message);

        public void Error(string message, params object[] objs)
            => throw new NotSupportedException(message);

        public void Error(Exception exception, string message, params object[] objs)
            => throw new NotSupportedException(message);
    }

    private sealed class NoAdvancedApi : IAdvancedApi
    {
        public IWorkersApi Workers => throw new NotSupportedException();

        public ITopicsApi Topics => throw new NotSupportedException();

        public IRoutingApi Routing => throw new NotSupportedException();

        public ITransportMessageApi TransportMessage => throw new NotSupportedException();

        public Rebus.DataBus.IDataBus DataBus => throw new NotSupportedException();

        public ISyncBus SyncBus => throw new NotSupportedException();
    }
}
