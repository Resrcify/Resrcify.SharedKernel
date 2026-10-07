using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Retry;
using Rebus.Retry.Simple;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Broker;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// Consumes one request queue on its own bus, under its own rate limit, and replies to each
/// request with its scatter headers copied over. One host per request type gives every type its own
/// limit per service instance; competing instances on the same queue add capacity.
/// </summary>
/// <remarks>
/// While the configured health check is unhealthy, the bus is disposed rather than paused: disposing
/// closes the connection, so RabbitMQ hands this instance's prefetched messages to the others.
/// (Pausing the workers keeps the connection open and strands them.) A new bus starts once healthy.
/// Failures are answered or tried again without exceptions: a failure that is the request's fault
/// (<see cref="RateLimitedQueueOptions.AnswerFailure"/>) is answered at once; any other (or a responder that threw) is
/// sent back to the queue for another try, through the rate limiter, on whichever instance takes it, and answered with
/// its errors after the last try. A message the bus itself can't handle (unreadable) is dropped
/// (<see cref="DropFailedRequestErrorHandler"/>).
/// <para>
/// The handler's cancellation token fires when the queue stops consuming (the request goes back to the queue, for
/// another instance) and when the request's time-to-live passes (its requester has stopped waiting, so it is
/// dropped unanswered instead of spending the upstream's budget on a reply nobody reads).
/// </para>
/// <para>
/// A bus that fails to start (RabbitMQ unreachable while the health gate says healthy) is logged and tried again,
/// 5 s doubling to 1 min; the queue isn't consuming meanwhile, and its host keeps running. A responder (or a handler
/// behind it) may take anything that needs <c>IBus</c>, e.g. <c>IEventBus</c>: it gets this queue's bus.
/// </para>
/// </remarks>
internal sealed partial class RateLimitedQueueHost<TRequest, TResponse>(
    string queueName,
    RateLimitedQueueOptions options,
    MessageBusSettings settings,
    IServiceProvider serviceProvider,
    Func<IServiceProvider, IRequestResponder<TRequest, TResponse>> responderFactory,
    ILogger<RateLimitedQueueHost<TRequest, TResponse>> logger)
    : BackgroundService, IQueueConsumer
    where TRequest : class
    where TResponse : class
{
    private static TimeSpan FirstStartRetryDelay => TimeSpan.FromSeconds(5);

    private static TimeSpan MaxStartRetryDelay => TimeSpan.FromMinutes(1);

    private BuiltinHandlerActivator? _activator;
    private RateLimiter? _limiter;
    private CancellationTokenSource? _consuming;
    private HealthGate? _healthGate;
    private TaskCompletionSource _wake = NewSignal();
    private int _restartRequested;
    private int _tries = 5;
    private TimeSpan _startRetryDelay = FirstStartRetryDelay;
    private DateTimeOffset? _startRetryAt;

    /// <summary>Whether this instance is currently consuming. For tests and diagnostics.</summary>
    public bool IsConsuming => _activator is not null;

    public string QueueName => queueName;

    /// <remarks>
    /// The queue follows its health-check tag's <see cref="HealthGate"/>, which every queue on the tag shares: checked
    /// every <see cref="RateLimitedQueueOptions.HealthCheckInterval"/>, and also right after a request fails (at most
    /// once a second). An instance that lost its upstream then stops taking requests within about a second, instead of
    /// failing them until the next scheduled check, so its requests are retried by the healthy instances rather than
    /// used up here. When RabbitMQ is back after a lost connection, the bus restarts, so it consumes again at once (see
    /// <see cref="BrokerConnectionWatcher"/>).
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watcher = serviceProvider.GetService<BrokerConnectionWatcher>();
        var gates = serviceProvider.GetRequiredService<HealthGates>();
        var tag = options.HealthCheckTag;
        _healthGate = tag is null ? null : gates.Join(tag, options.HealthCheckInterval);
        watcher?.Recovered += OnBrokerRecovered;
        MessageBusDiagnostics.Track(this);
        try
        {
            if (_healthGate is not null)
                await _healthGate.FirstCheck.WaitAsync(stoppingToken);
            var stopped = Task.Delay(Timeout.Infinite, stoppingToken);
            var time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            while (!stoppingToken.IsCancellationRequested)
            {
                // Take the signals before acting, so a check or a restart request meanwhile wakes the next round.
                var wake = NewSignal();
                Interlocked.Exchange(ref _wake, wake);
                var nextCheck = _healthGate?.NextCheck ?? Task.Delay(Timeout.Infinite, stoppingToken);

                if (Interlocked.Exchange(ref _restartRequested, 0) == 1 && _activator is not null)
                {
                    StopBus();
                    LogRestarting(queueName);
                }

                var healthy = _healthGate?.IsHealthy ?? true;
                if (healthy && _activator is null && DueToStart(time))
                    TryStartBus(time);
                else if (!healthy && _activator is not null)
                    StopBus();

                var retry = _startRetryAt is { } at && _activator is null
                    ? Task.Delay(Max(at - time.GetUtcNow(), TimeSpan.Zero), time, stoppingToken)
                    : stopped;
                await Task.WhenAny(nextCheck, wake.Task, retry, stopped);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
        finally
        {
            watcher?.Recovered -= OnBrokerRecovered;
            MessageBusDiagnostics.Untrack(this);
            if (tag is not null)
                gates.Leave(tag);
        }
    }

    private bool DueToStart(TimeProvider time)
        => _startRetryAt is not { } at || at <= time.GetUtcNow();

    // A start that fails (RabbitMQ unreachable, a queue that can't be declared) is tried again later, instead of
    // faulting the host: the queue would never consume again, while the health check said it did.
    private void TryStartBus(TimeProvider time)
    {
        try
        {
            StartBus();
            _startRetryAt = null;
            _startRetryDelay = FirstStartRetryDelay;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogStartFailed(exception, queueName, _startRetryDelay.TotalSeconds);
            _startRetryAt = time.GetUtcNow() + _startRetryDelay;
            _startRetryDelay = Max(Min(_startRetryDelay * 2, MaxStartRetryDelay), FirstStartRetryDelay);
        }
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second)
        => first > second ? first : second;

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
        => first < second ? first : second;

    private void OnBrokerRecovered()
    {
        Volatile.Write(ref _restartRequested, 1);
        Volatile.Read(ref _wake).TrySetResult();
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        StopBus();
    }

    public override void Dispose()
    {
        StopBus();
        base.Dispose();
    }

    private void StartBus()
    {
        var prefetch = options.EffectivePrefetch;
        var strategy = settings.ResolveConfigurationStrategy(serviceProvider);
        var consuming = new CancellationTokenSource();
        var limiter = options.RateLimiter.CreateLimiter(queueName, options);
        var activator = new BuiltinHandlerActivator();
        activator.Handle<TRequest>((bus, context, request) => HandleAsync(bus, context, request, consuming.Token));
        try
        {
            ConfigureBus(activator, limiter, strategy, prefetch, consuming.Token);
        }
        catch
        {
            // Nothing half-started is kept: the queue isn't consuming, and IsConsuming says so.
            activator.Dispose();
            consuming.Dispose();
            limiter.Dispose();
            throw;
        }

        _consuming = consuming;
        _limiter = limiter;
        _activator = activator;
        LogStarted(queueName, options.PerSecond, prefetch);
    }

    private void ConfigureBus(
        BuiltinHandlerActivator activator,
        RateLimiter limiter,
        IBusConfigurationStrategy strategy,
        int prefetch,
        CancellationToken consuming)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var rateLimitStep = new RateLimitStep(limiter, queueName, time, consuming);
        Configure.With(activator)
            .Logging(logging => logging.Use(new RebusLoggerFactory(loggerFactory)))
            .Transport(transport => settings.ConfigureTransport(
                transport,
                serviceProvider,
                queueName,
                strategy,
                rabbitMq => rabbitMq.Prefetch(prefetch)))
            .Serialization(serializer => settings.ConfigureSerialization(serializer, serviceProvider))
            .Options(configure =>
            {
                configure.SetNumberOfWorkers(1);
                configure.SetMaxParallelism(prefetch);
                // A failed request is tried again (sent back to the queue) as often as the strategy would deliver it.
                configure.Decorate(context =>
                {
                    var retry = context.Get<RetryStrategySettings>();
                    Volatile.Write(ref _tries, Math.Max(1, retry.MaxDeliveryAttempts));
                    return retry;
                });
                configure.Decorate<IPipeline>(context => new PipelineStepInjector(context.Get<IPipeline>())
                    .OnReceive(rateLimitStep, PipelineRelativePosition.Before, typeof(DispatchIncomingMessageStep)));
                settings.ConfigureEveryBus(configure, serviceProvider);
                configure.Register<IErrorHandler>(_ => new DropFailedRequestErrorHandler(queueName, logger));
                strategy.ConfigureOptions(configure);
            })
            .Start();
    }

    /// <summary>
    /// In-flight requests are cancelled and go back to the queue, as do the prefetched ones when the connection
    /// closes, so the other instances take them.
    /// </summary>
    private void StopBus()
    {
        if (_activator is null)
            return;
        _consuming?.Cancel();
        _activator.Dispose();
        _activator = null;
        _consuming?.Dispose();
        _consuming = null;
        _limiter?.Dispose();
        _limiter = null;
        LogStopped(queueName);
    }

    private async Task HandleAsync(IBus bus, IMessageContext context, TRequest request, CancellationToken consuming)
    {
        var time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var remaining = MessageDeadline.Remaining(context.Headers, time.GetUtcNow()) ?? Timeout.InfiniteTimeSpan;
        if (remaining != Timeout.InfiniteTimeSpan && remaining <= TimeSpan.Zero)
        {
            LogExpired(queueName);
            MessageBusDiagnostics.RecordRequest(queueName, RequestOutcome.Expired, TimeSpan.Zero);
            return;   // acknowledged unanswered: its requester has stopped waiting
        }

        // What resolves IBus during this request (IEventBus, Rebus.ServiceProvider's factory) finds this queue's bus:
        // its own isn't the container's, so nothing else puts it in the step context.
        context.IncomingStepContext.Save(bus);
        using var deadlinePassed = new CancellationTokenSource(remaining, time);
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(consuming, deadlinePassed.Token);

        var started = time.GetTimestamp();
        var outcome = await HandleWithOutcomeAsync(request, time, started, consuming, expiry.Token);
        if (outcome is not { } handled)
            return;   // expired while being handled

        var (response, failedUnexpectedly) = handled;
        var attempt = RequestTries.Read(context.Headers);
        var mayPassNextTime = response.IsFailure && (failedUnexpectedly || !options.AnswerFailure(response.Errors));
        if (mayPassNextTime && attempt < Volatile.Read(ref _tries))
        {
            await TryAgainAsync(bus, attempt, response.Errors, time, started, consuming, expiry.Token);
            return;
        }

        var headers = ScatterHeaders.CopyFrom(context.Headers);
        if (response.IsSuccess)
            await bus.Reply(response.Value, headers);
        else
            await bus.Reply(ScatterRequestFailed.From(response.Errors), headers);
        MessageBusDiagnostics.RecordRequest(queueName, AnsweredOutcome(response, mayPassNextTime), time.GetElapsedTime(started));
    }

    private static RequestOutcome AnsweredOutcome(Result response, bool mayPassNextTime)
    {
        if (response.IsSuccess)
            return RequestOutcome.Success;
        return mayPassNextTime ? RequestOutcome.GaveUp : RequestOutcome.Failure;
    }

    /// <summary>
    /// Sends the request back to its queue for another try, after a wait. Any instance may take it (this one may have
    /// lost its upstream, so its health gate checks now), and it passes the rate limiter again, so a retry spends the
    /// budget like any request. No exception: the bus acknowledges this delivery, and the copy carries the try count.
    /// </summary>
    private async Task TryAgainAsync(
        IBus bus,
        int attempt,
        IReadOnlyList<Error> errors,
        TimeProvider time,
        long started,
        CancellationToken consuming,
        CancellationToken expiry)
    {
        _healthGate?.RequestCheck();
        LogTryingAgain(queueName, attempt, string.Join(", ", errors.Select(error => $"{error.Code} ({error.Type})")));
        try
        {
            await Task.Delay(RetryDelay(attempt), time, expiry);
        }
        catch (OperationCanceledException) when (!consuming.IsCancellationRequested)
        {
            LogExpired(queueName);
            MessageBusDiagnostics.RecordRequest(queueName, RequestOutcome.Expired, time.GetElapsedTime(started));
            return;
        }
        await bus.Advanced.TransportMessage.Forward(queueName, RequestTries.Next(attempt));
        MessageBusDiagnostics.RecordRequest(queueName, RequestOutcome.Retried, time.GetElapsedTime(started));
    }

    /// <summary>0.5 s before the second try, doubling: 0.5, 1, 2, 4 s.</summary>
    internal static TimeSpan RetryDelay(int failedAttempt)
        => TimeSpan.FromMilliseconds(500 * Math.Pow(2, failedAttempt - 1));

    /// <summary>
    /// The handler's result, and whether it threw (an exception becomes a failure, answered or tried again like any);
    /// <see langword="null"/> when the request expired while being handled.
    /// </summary>
    private async Task<(Result<TResponse> Response, bool FailedUnexpectedly)?> HandleWithOutcomeAsync(
        TRequest request,
        TimeProvider time,
        long started,
        CancellationToken consuming,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var handler = responderFactory(scope.ServiceProvider);
        try
        {
            return (await handler.HandleAsync(request, cancellationToken), false);
        }
        catch (OperationCanceledException) when (consuming.IsCancellationRequested)
        {
            // The queue stopped: the exception fails the message, so it goes back to the queue for another instance.
            MessageBusDiagnostics.RecordRequest(queueName, RequestOutcome.Cancelled, time.GetElapsedTime(started));
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogExpired(queueName);
            MessageBusDiagnostics.RecordRequest(queueName, RequestOutcome.Expired, time.GetElapsedTime(started));
            return null;
        }
        catch (Exception exception)
        {
            LogResponderFailedOnce(exception);
            return (Result.Failure<TResponse>(Error.Failure(
                $"{typeof(TRequest).Name}.ResponderFailed",
                $"The responder failed ({exception.GetType().Name}); see its logs.")), true);
        }
    }

    // At Error with the stack unless a mediator behavior behind the responder did so already; turned into a failure here,
    // the exception's journey ends, so its mark is released.
    private void LogResponderFailedOnce(Exception exception)
    {
        if (LoggedExceptions.Claim(exception))
        {
            LogResponderFailed(exception, queueName);
        }
        else if (logger.IsEnabled(LogLevel.Debug))
        {
            var exceptionType = exception.GetType().Name;
            LogResponderFailedLoggedAlready(queueName, exceptionType);
        }

        LoggedExceptions.Release(exception);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A request on {Queue} failed (try {Attempt}: {Errors}); sending it back to the queue for another try")]
    private partial void LogTryingAgain(string queue, int attempt, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "The responder of {Queue} threw; the request is tried again, then answered with a failure")]
    private partial void LogResponderFailed(Exception exception, string queue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The responder of {Queue} threw {ExceptionType} (logged already); the request is tried again, then answered with a failure")]
    private partial void LogResponderFailedLoggedAlready(string queue, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not start the bus of {Queue}; it tries again in {DelaySeconds} s")]
    private partial void LogStartFailed(Exception exception, string queue, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped a request on {Queue} unanswered: its requester has stopped waiting")]
    private partial void LogExpired(string queue);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarting the bus of {Queue}: RabbitMQ is back")]
    private partial void LogRestarting(string queue);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Queue} at {PerSecond}/s with prefetch {Prefetch}")]
    private partial void LogStarted(string queue, int perSecond, int prefetch);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped consuming {Queue}; its messages go back to the other instances")]
    private partial void LogStopped(string queue);
}
