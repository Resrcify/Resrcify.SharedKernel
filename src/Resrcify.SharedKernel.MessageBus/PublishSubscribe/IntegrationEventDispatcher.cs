using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Exceptions;
using Rebus.Extensions;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// Rebus' handler for an event this service subscribes to: runs every
/// <see cref="IIntegrationEventHandler{TEvent}"/> for it, under its <see cref="SubscriptionOptions{TEvent}"/>, and,
/// when the service skips duplicates, only if this service hasn't handled the same message before.
/// Registered by <c>AddEventHandlers</c> and <c>ForwardEvent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Failures are handled without exceptions: a handler's failed <see cref="Result"/> (or an exception, which is caught)
/// is either the event's fault (logged, not retried) or retried in place, and after the last try the message is
/// dead-lettered to the service's error queue. Only a shutdown goes back to the bus (the message returns to its queue).
/// </para>
/// <para>
/// Each try runs in a DI scope of its own (a failed try's tracked changes and domain events don't reach the next one's
/// save) and a Rebus transaction of its own: what a try sends or publishes goes out only when it succeeds, so a failed
/// try's messages are dropped rather than sent along with the next. Waits between tries double from 0.5 s up to 4 s.
/// </para>
/// <para>
/// Skipping duplicates never costs an event: a claim store out of reach (the cache is down) means the event is handled
/// anyway (a duplicate may get through), and a claim that can't be released is logged and left to expire.
/// </para>
/// </remarks>
internal sealed partial class IntegrationEventDispatcher<TEvent>(
    IEnumerable<IIntegrationEventHandler<TEvent>> handlers,
    MessageBusSettings settings,
    ReceiveOrder receiveOrder,
    EventsInFlight inFlight,
    TimeProvider time,
    IBus bus,
    IServiceProvider serviceProvider,
    ILogger<IntegrationEventDispatcher<TEvent>> logger)
    : IHandleMessages<TEvent>
    where TEvent : class
{
    public async Task Handle(TEvent message)
    {
        var wireName = settings.WireNameOf(typeof(TEvent));
        var context = MessageContext.Current;
        var cancellationToken = context?.GetCancellationToken() ?? CancellationToken.None;
        var started = time.GetTimestamp();
        WarnIfAnotherServicePublishesTheSameName(wireName, context);

        var options = settings.SubscriptionOptionsOf<TEvent>();
        var number = options.InPublishOrder ? ReceiveOrder.NumberOf(context) : null;
        if (number is { } waitFor)
            await receiveOrder.WaitForTurnAsync(waitFor, cancellationToken);

        Task handling;
        try
        {
            // Takes the event's place in its partition before returning, so admitting it next lets the following
            // events take theirs after it.
            handling = options.RunAsync(
                message,
                () => HandleOnceAsync(message, wireName, context, started, cancellationToken));
        }
        finally
        {
            if (number is { } admitted)
                receiveOrder.Admit(admitted);
        }
        await handling;
    }

    private async Task HandleOnceAsync(
        TEvent message,
        string wireName,
        IMessageContext? context,
        long started,
        CancellationToken cancellationToken)
    {
        if (HandledKey(context) is not { } handled)
        {
            var outcome = await RunHandlersAsync(message, wireName, cancellationToken);
            MessageBusDiagnostics.RecordEvent(wireName, outcome, time.GetElapsedTime(started));
            return;
        }

        // A copy being handled on this instance right now is a duplicate whatever the cache says.
        if (!inFlight.TryStart(handled))
        {
            MessageBusDiagnostics.RecordEvent(wireName, EventOutcome.Duplicate, time.GetElapsedTime(started));
            return;
        }
        try
        {
            await HandleUnlessHandledAsync(message, wireName, handled, started, cancellationToken);
        }
        finally
        {
            inFlight.Finish(handled);
        }
    }

    /// <summary>
    /// Claims the event's handled key and handles it, or skips it when another copy has claimed it (handled it, or is
    /// handling it on another instance).
    /// </summary>
    private async Task HandleUnlessHandledAsync(
        TEvent message,
        string wireName,
        string handled,
        long started,
        CancellationToken cancellationToken)
    {
        var claims = ClaimStores.Of(serviceProvider)
            ?? throw new InvalidOperationException("SkipDuplicateEvents needs an IClaimStore (checked at start-up).");
        // Claimed before handling, in one step, so a copy arriving meanwhile is skipped too.
        if (await TryClaimAsync(claims, handled, wireName, cancellationToken) is not { } claimed)
        {
            // The store is out of reach: handled anyway, rather than sent back until it is dead-lettered unhandled.
            var unclaimed = await RunHandlersAsync(message, wireName, cancellationToken);
            MessageBusDiagnostics.RecordEvent(wireName, unclaimed, time.GetElapsedTime(started));
            return;
        }
        if (!claimed)
        {
            MessageBusDiagnostics.RecordEvent(wireName, EventOutcome.Duplicate, time.GetElapsedTime(started));
            return;
        }

        EventOutcome outcome;
        try
        {
            outcome = await RunHandlersAsync(message, wireName, cancellationToken);
        }
        catch
        {
            // Not handled (a shutdown, or the error queue out of reach): the message goes back to its queue, and its
            // next delivery is handled rather than skipped.
            await ReleaseAsync(claims, handled, wireName);
            throw;
        }

        // Released when it went to the error queue: moved back from there, it is handled again rather than skipped.
        if (outcome == EventOutcome.Error)
            await ReleaseAsync(claims, handled, wireName);
        MessageBusDiagnostics.RecordEvent(wireName, outcome, time.GetElapsedTime(started));
    }

    /// <summary>Whether the event was claimed; <see langword="null"/> when the claim store couldn't say.</summary>
    private async Task<bool?> TryClaimAsync(IClaimStore claims, string handled, string wireName, CancellationToken cancellationToken)
    {
        try
        {
            return await claims.TryClaimForAsync(handled, settings.RememberHandledEventsFor!.Value, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            LogClaimFailed(exception, wireName);
            return null;
        }
    }

    // A release that fails mustn't fail the delivery: thrown after a dead-letter, the redelivery would find the claim and
    // be acknowledged as a duplicate, losing the event. Left held, the claim expires after RememberHandledEventsFor.
    private async Task ReleaseAsync(IClaimStore claims, string handled, string wireName)
    {
        try
        {
            await claims.ReleaseAsync(handled, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogReleaseFailed(exception, wireName, settings.RememberHandledEventsFor!.Value);
        }
    }

    /// <summary>
    /// Runs the handlers one after the other. A retry starts again at the handler that failed, so the ones before it
    /// don't run twice.
    /// </summary>
    private async Task<EventOutcome> RunHandlersAsync(TEvent message, string wireName, CancellationToken cancellationToken)
    {
        var outcome = EventOutcome.Success;
        var names = handlers.Select(handler => handler.GetType().Name).ToList();
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            for (var attempt = 1; attempt <= settings.EventTries; attempt++)
            {
                var failure = await TryHandleAsync(index, message, cancellationToken);
                if (failure is null)
                    break;

                if (failure.IsTheEventsFault)
                {
                    LogRejected(wireName, name, failure.Describe());
                    outcome = EventOutcome.Rejected;
                    break;
                }

                if (attempt == settings.EventTries)
                {
                    LogDeadLettered(OnceLogged(failure.Exception), wireName, name, attempt, failure.Describe());
                    await bus.Advanced.TransportMessage.Deadletter($"{name} failed {attempt} times: {failure.Describe()}");
                    return EventOutcome.Error;
                }

                LogRetryInPlace(NotLoggedYet(failure.Exception), wireName, name, attempt, failure.Describe());
                await Task.Delay(RetryDelay(attempt), time, cancellationToken);
            }
        }
        return outcome;
    }

    /// <summary>
    /// One try, of the handler at <paramref name="index"/> in a DI scope of its own and a Rebus transaction of its own:
    /// <see langword="null"/> when it succeeded (what it sent goes out then), else how it failed (a failed result or an
    /// exception; what it sent is dropped).
    /// </summary>
    private async Task<HandlerFailure?> TryHandleAsync(
        int index,
        TEvent message,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().ElementAt(index);
        var incoming = AmbientTransactionContext.Current;
        using var outsideTheDelivery = new RebusTransactionScopeSuppressor();
        using var sending = new RebusTransactionScope();
        // The try's transaction still knows the message it handles (MessageContext.Current, headers, IBus).
        if (incoming?.Items.TryGetValue(StepContext.StepContextKey, out var stepContext) == true)
            sending.TransactionContext.Items[StepContext.StepContextKey] = stepContext;
        try
        {
            var result = await handler.HandleAsync(message, cancellationToken);
            if (!result.IsSuccess)
                return new HandlerFailure(result.Errors, Exception: null);

            await sending.CompleteAsync();
            return null;
        }
        // A shutdown isn't the handler's failure: the message goes back to its queue for the next instance.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new HandlerFailure([], exception);
        }
    }

    // The mediator's behaviors may have logged it at Error already: then only the line, without the exception again.
    // A try's exception ends there (it becomes a failure), so its mark is released.
    private static Exception? OnceLogged(Exception? exception)
    {
        if (exception is null)
            return null;
        var first = LoggedExceptions.Claim(exception);
        LoggedExceptions.Release(exception);
        return first ? exception : null;
    }

    private static Exception? NotLoggedYet(Exception? exception)
        => exception is null || LoggedExceptions.IsLogged(exception) ? null : exception;

    /// <summary>0.5 s before the second try, doubling up to 4 s: 0.5, 1, 2, 4, 4, ... s.</summary>
    internal static TimeSpan RetryDelay(int failedAttempt)
    {
        var delay = TimeSpan.FromMilliseconds(500);
        for (var doubled = 1; doubled < failedAttempt && delay < MaxRetryDelay; doubled++)
            delay *= 2;
        return delay < MaxRetryDelay ? delay : MaxRetryDelay;
    }

    /// <summary>
    /// The longest wait between two tries, whatever the retry strategy's number of tries: the event is held unacknowledged
    /// meanwhile (a worker, a prefetch slot, its partition), and RabbitMQ closes a channel holding one past its
    /// consumer timeout.
    /// </summary>
    private static TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(4);

    /// <summary>How one try failed: the handler's errors, or the exception it threw.</summary>
    private sealed record HandlerFailure(IReadOnlyList<Error> Errors, Exception? Exception)
    {
        public bool IsTheEventsFault
            => Exception is null && MessageFailures.AreTheMessagesFault(Errors);

        public string Describe()
            => Exception is not null
                ? $"{Exception.GetType().Name}: {Exception.Message}"
                : string.Join(", ", Errors.Select(error => $"{error.Code} ({error.Type}): {error.Message}"));
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Event}: {Handler} rejected it ({Errors}); another try would fail the same way, so it isn't retried")]
    private partial void LogRejected(string @event, string handler, string errors);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Event}: {Handler} failed (try {Attempt}: {Failure}); trying it again in its place")]
    private partial void LogRetryInPlace(Exception? exception, string @event, string handler, int attempt, string failure);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{Event}: {Handler} failed {Attempt} times ({Failure}); moved to the error queue")]
    private partial void LogDeadLettered(Exception? exception, string @event, string handler, int attempt, string failure);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Event}: the claim store is out of reach, so it is handled without skipping duplicates")]
    private partial void LogClaimFailed(Exception exception, string @event);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Event}: could not release its claim; a copy moved back from the error queue within {RememberFor} is skipped")]
    private partial void LogReleaseFailed(Exception exception, string @event, TimeSpan rememberFor);

    /// <summary>
    /// Event names are global on the broker: if two services publish events of the same name, this service gets both,
    /// though it expects one of them. Warns (once per service pair) and counts it.
    /// </summary>
    private void WarnIfAnotherServicePublishesTheSameName(string wireName, IMessageContext? context)
    {
        if (context is null || !context.Headers.TryGetValue(EventBus.PublisherHeader, out var publisher))
            return;
        if (settings.OtherPublisherOf(wireName, publisher) is not { } first)
            return;
        MessageBusDiagnostics.RecordNameClash(wireName, first, publisher);
        LogNameClash(wireName, first, publisher);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Two services publish an event called {Event}: {First} and {Second}. Prefix event class names with their owner (e.g. ShardRankChanged)")]
    private partial void LogNameClash(string @event, string first, string second);

    /// <summary>The cache key that marks this message handled by this service, or <see langword="null"/> when not skipping duplicates.</summary>
    private string? HandledKey(IMessageContext? context)
        => settings.RememberHandledEventsFor is null
           || context is null
           || !context.Headers.TryGetValue(Headers.MessageId, out var messageId)
            ? null
            : $"messagebus:handled:{settings.InputQueue}:{messageId}";
}
