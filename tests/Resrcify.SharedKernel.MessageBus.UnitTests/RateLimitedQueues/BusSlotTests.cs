using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

/// <summary>
/// A rate-limited queue's bus is taken out of its slot by one caller only, and none stays in a closed slot. The
/// concurrent tests release their callers together (a barrier) and check what holds in every interleaving, round after
/// round: a correct slot passes every round, and one whose check and take aren't one step fails within the rounds.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class BusSlotTests
{
    private const int Rounds = 1000;
    private const int Callers = 8;

    [Fact]
    public void TryPut_ShouldPutTheBus_WhenTheSlotIsOpenAndEmpty()
    {
        var slot = new BusSlot<Bus>();
        var bus = new Bus();

        slot.TryPut(bus).ShouldBeTrue();

        slot.HasBus.ShouldBeTrue();
        slot.Take().ShouldBeSameAs(bus);
        slot.HasBus.ShouldBeFalse();
    }

    [Fact]
    public void TryPut_ShouldPutANewBus_WhenTheLastOneWasTakenOut()
    {
        // The health gate stops the queue and starts it again.
        var slot = new BusSlot<Bus>();
        slot.TryPut(new Bus());
        slot.Take();
        var restarted = new Bus();

        slot.TryPut(restarted).ShouldBeTrue();

        slot.Take().ShouldBeSameAs(restarted);
    }

    [Fact]
    public void TryPut_ShouldRefuseTheBus_WhenTheSlotIsClosed()
    {
        // The host stopped: a bus that started after it stays the caller's, to stop.
        var slot = new BusSlot<Bus>();
        slot.Close();

        slot.TryPut(new Bus()).ShouldBeFalse();

        slot.HasBus.ShouldBeFalse();
        slot.IsClosed.ShouldBeTrue();
    }

    [Fact]
    public void Close_ShouldTakeOutTheBus_WhenThereIsOne()
    {
        var slot = new BusSlot<Bus>();
        var bus = new Bus();
        slot.TryPut(bus);

        slot.Close().ShouldBeSameAs(bus);
        slot.Close().ShouldBeNull();
        slot.Take().ShouldBeNull();
    }

    [Fact]
    public void Take_ShouldGiveTheBusToOneCallerOnly_WhenManyStopItAtOnce()
    {
        // The loop stepping aside, the host's StopAsync twice and its Dispose: Take and Close, all at once.
        var slots = Enumerable.Range(0, Rounds).Select(_ => new BusSlot<Bus>()).ToArray();
        var buses = slots.Select(Filled).ToArray();
        var taken = Enumerable.Range(0, Rounds).Select(_ => new Bus?[Callers]).ToArray();

        RunTogether(Callers, (caller, round) => taken[round][caller] = caller % 2 == 0
            ? slots[round].Take()
            : slots[round].Close());

        for (var round = 0; round < Rounds; round++)
        {
            var takers = Enumerable.Range(0, Callers).Count(caller => taken[round][caller] is not null);
            takers.ShouldBe(1, $"round {round}: the bus was taken out {takers} times");
            Enumerable.Range(0, Callers)
                .Select(caller => taken[round][caller])
                .OfType<Bus>()
                .Single()
                .ShouldBeSameAs(buses[round]);
            slots[round].HasBus.ShouldBeFalse();
        }
    }

    [Fact]
    public void TryPut_ShouldLeaveNoBusInTheSlot_WhenTheSlotClosesWhileTheBusIsPut()
    {
        // The health gate restarting the queue as the host stops: a bus put while the slot closes is stopped by exactly
        // one of them, and none stays in the slot.
        var slots = Enumerable.Range(0, Rounds).Select(_ => new BusSlot<Bus>()).ToArray();
        var buses = Enumerable.Range(0, Rounds).Select(_ => new Bus()).ToArray();
        var put = new bool[Rounds];
        var closedWith = new Bus?[Rounds];

        RunTogether(2, (caller, round) =>
        {
            if (caller == 0)
                put[round] = slots[round].TryPut(buses[round]);
            else
                closedWith[round] = slots[round].Close();
        });

        for (var round = 0; round < Rounds; round++)
        {
            // Whoever ends up with the bus stops it: the close (it took it out), or the putter (it was refused).
            var stoppers = (put[round] ? 0 : 1) + (closedWith[round] is null ? 0 : 1);
            stoppers.ShouldBe(1, $"round {round}: put {put[round]}, closed with the bus {closedWith[round] is not null}");
            slots[round].HasBus.ShouldBeFalse($"round {round}: a bus stayed in a closed slot");
        }
    }

    private static Bus Filled(BusSlot<Bus> slot)
    {
        var bus = new Bus();
        slot.TryPut(bus).ShouldBeTrue();
        return bus;
    }

    /// <summary>
    /// Runs <paramref name="act"/> for every round on <paramref name="callers"/> threads of their own, which a barrier
    /// releases together at the start of each round.
    /// </summary>
    private static void RunTogether(
        int callers,
        Action<int, int> act)
    {
        using var roundStarts = new Barrier(callers);
        var failures = new Exception?[callers];
        var threads = Enumerable.Range(0, callers)
            .Select(caller => new Thread(() =>
            {
                try
                {
                    for (var round = 0; round < Rounds; round++)
                    {
                        roundStarts.SignalAndWait();
                        act(caller, round);
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures[caller] = exception;
                    roundStarts.RemoveParticipant();
                }
            }))
            .ToArray();

        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        failures.OfType<Exception>().ShouldBeEmpty();
    }

    /// <summary>A bus, as the slot sees one: a reference to hand over.</summary>
    internal sealed class Bus
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
