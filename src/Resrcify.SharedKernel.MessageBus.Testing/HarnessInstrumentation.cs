using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Pipeline.Send;
using Rebus.Retry;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>Hooks the harness into every bus of the service.</summary>
internal sealed class HarnessInstrumentation(MessageBusTestHarness harness) : IBusInstrumentation
{
    public void Configure(OptionsConfigurer options)
    {
        options.Decorate<ITransport>(context =>
        {
            var transport = context.Get<ITransport>();
            if (harness.TryFailStart(transport.Address))
                throw new InvalidOperationException($"The bus on '{transport.Address}' failed to start (FailStart, the test harness).");
            return new HarnessTransport(transport, harness.Network);
        });
        options.Decorate<IPipeline>(context =>
        {
            var queue = context.Get<ITransport>().Address;
            return new PipelineStepInjector(context.Get<IPipeline>())
                .OnReceive(
                    new HarnessIncomingStep(harness, queue),
                    PipelineRelativePosition.Before,
                    typeof(DispatchIncomingMessageStep))
                .OnSend(
                    new HarnessOutgoingStep(harness),
                    PipelineRelativePosition.Before,
                    typeof(SerializeOutgoingMessageStep));
        });
        options.Decorate<IErrorHandler>(context => new HarnessErrorHandler(
            context.Get<IErrorHandler>(),
            harness,
            context.Get<ITransport>().Address));
    }
}

/// <summary>Counts a message as being handled from the moment it leaves its queue until its handling is over.</summary>
internal sealed class HarnessTransport(ITransport transport, MessageBusTestNetwork network) : ITransport
{
    public string Address
        => transport.Address;

    public void CreateQueue(string address)
        => transport.CreateQueue(address);

    public Task Send(string destinationAddress, TransportMessage message, ITransactionContext context)
        => transport.Send(destinationAddress, message, context);

    public async Task<TransportMessage?> Receive(ITransactionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = await transport.Receive(context, cancellationToken);
        if (message is not null)
        {
            network.Received();
            // After the commit, which sends what the handler sent: the network is never idle in between.
            context.OnDisposed(_ => network.Handled());
        }

        return message;
    }
}

/// <summary>Records each delivery to the handlers, and fails the ones <see cref="MessageBusTestHarness.FailNext{TMessage}"/> asked for.</summary>
internal sealed class HarnessIncomingStep(MessageBusTestHarness harness, string? queue) : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var message = context.Load<Message>();
        message.Headers.TryGetValue(Headers.MessageId, out var messageId);
        harness.Handling(messageId, message.Body);
        if (harness.TryFailDelivery(message.Body, out var injected))
        {
            Fault(message, messageId, injected);
            throw injected;
        }

        try
        {
            await next();
        }
        catch (Exception exception)
        {
            Fault(message, messageId, exception);
            throw;
        }

        harness.Done(messageId);
        harness.Consumed.Add(RecordedMessage.Received(message, queue));
    }

    private void Fault(Message message, string? messageId, Exception exception)
    {
        harness.Failed(messageId, exception);
        harness.Faulted.Add(RecordedMessage.Received(message, queue, exception));
    }
}

/// <summary>Records each message sent or published, once its transaction commits (once it really left).</summary>
internal sealed class HarnessOutgoingStep(MessageBusTestHarness harness) : IOutgoingStep
{
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var message = context.Load<Message>();
        var destinations = context.Load<DestinationAddresses>()?.ToList() ?? [];
        await next();

        var published = message.Headers.TryGetValue(Headers.Intent, out var intent)
            && intent == Headers.IntentOptions.PublishSubscribe;
        var recording = published ? harness.Published : harness.Sent;
        var recorded = RecordedMessage.Outgoing(message, destinations);
        context.Load<ITransactionContext>().OnCommit(_ =>
        {
            recording.Add(recorded);
            return Task.CompletedTask;
        });
    }
}

/// <summary>Records each message given up on, then lets the bus' own error handler deal with it.</summary>
internal sealed class HarnessErrorHandler(IErrorHandler inner, MessageBusTestHarness harness, string? queue) : IErrorHandler
{
    public async Task HandlePoisonMessage(TransportMessage transportMessage, ITransactionContext transactionContext, ExceptionInfo exception)
    {
        ArgumentNullException.ThrowIfNull(transportMessage);
        transportMessage.Headers.TryGetValue(Headers.MessageId, out var messageId);
        var (body, lastFailure) = harness.GiveUp(messageId);
        await inner.HandlePoisonMessage(transportMessage, transactionContext, exception);
        harness.DeadLettered.Add(new RecordedMessage(
            body,
            new System.Collections.Generic.Dictionary<string, string>(transportMessage.Headers, StringComparer.Ordinal),
            [],
            queue,
            lastFailure ?? new InvalidOperationException(exception?.Message)));
    }
}
