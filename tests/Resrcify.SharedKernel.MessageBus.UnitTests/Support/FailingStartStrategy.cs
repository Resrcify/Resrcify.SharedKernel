using System;
using System.Threading;
using Rebus.Config;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>
/// Fails the start of the buses whose queue <paramref name="failsFor"/> picks, as a queue that can't be declared does
/// (RabbitMQ unreachable, a node down): the next <see cref="Arm"/>ed starts throw, the others go through. Every other
/// bus starts as usual.
/// </summary>
internal sealed class FailingStartStrategy(Func<string?, bool> failsFor) : IBusConfigurationStrategy
{
    private int _failuresLeft;
    private int _failed;

    /// <summary>How many starts of a matching bus have failed so far.</summary>
    public int Failed => Volatile.Read(ref _failed);

    /// <summary>Fails the next <paramref name="starts"/> starts of a matching bus.</summary>
    public void Arm(int starts)
        => Volatile.Write(ref _failuresLeft, starts);

    public void ConfigureTransport(RabbitMqOptionsBuilder transport)
    {
    }

    public void ConfigureOptions(OptionsConfigurer options)
        => options.Decorate<ITransport>(context =>
        {
            var transport = context.Get<ITransport>();
            if (failsFor(transport.Address) && TryFail())
                throw new InvalidOperationException($"Queue declaration for '{transport.Address}' failed (the test's).");
            return transport;
        });

    private bool TryFail()
    {
        while (true)
        {
            var left = Volatile.Read(ref _failuresLeft);
            if (left <= 0)
                return false;
            if (Interlocked.CompareExchange(ref _failuresLeft, left - 1, left) == left)
            {
                Interlocked.Increment(ref _failed);
                return true;
            }
        }
    }
}
