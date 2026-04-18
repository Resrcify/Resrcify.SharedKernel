using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Extensions;
using Resrcify.SharedKernel.Messaging.Publishing;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Runtime;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MediatorRuntimeTests
{
    [Fact]
    public async Task Send_HandlesRequest_WithRegisteredHandler()
    {
        using var serviceProvider = BuildServiceProvider();

        var sender = serviceProvider.GetRequiredService<ISender>();
        var response = await sender.Send(new PingRequest(), CancellationToken.None);

        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Send_ExecutesPipelineBehaviors_AroundHandler()
    {
        using var serviceProvider = BuildServiceProvider();

        var sender = serviceProvider.GetRequiredService<ISender>();
        var trace = serviceProvider.GetRequiredService<ExecutionTrace>();

        await sender.Send(new PingRequest(), CancellationToken.None);

        trace.Steps.ShouldContain("First:Before");
        trace.Steps.ShouldContain("Second:Before");
        trace.Steps.ShouldContain("Handler");
        trace.Steps.ShouldContain("First:After");
        trace.Steps.ShouldContain("Second:After");

        var handlerIndex = trace.Steps.IndexOf("Handler");
        handlerIndex.ShouldBeGreaterThan(-1);

        trace.Steps.Take(handlerIndex).All(step => step.EndsWith("Before", StringComparison.Ordinal)).ShouldBeTrue();
        trace.Steps.Skip(handlerIndex + 1).All(step => step.EndsWith("After", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_InvokesAllNotificationHandlers()
    {
        using var serviceProvider = BuildServiceProvider();

        var publisher = serviceProvider.GetRequiredService<IPublisher>();
        var notifications = serviceProvider.GetRequiredService<NotificationTracker>();

        await publisher.Publish(new PingNotification(), CancellationToken.None);

        notifications.HandledBy.Count.ShouldBe(2);
        notifications.HandledBy.ShouldContain("FirstNotificationHandler");
        notifications.HandledBy.ShouldContain("SecondNotificationHandler");
    }

    [Fact]
    public async Task Publish_WithSequentialStrategy_ExecutesHandlersSequentially()
    {
        using var serviceProvider = BuildServiceProvider(NotificationPublishStrategy.Sequential);

        var publisher = serviceProvider.GetRequiredService<IPublisher>();
        var concurrency = serviceProvider.GetRequiredService<NotificationConcurrencyTracker>();

        await publisher.Publish(new ParallelPingNotification(), CancellationToken.None);

        concurrency.MaxConcurrency.ShouldBe(1);
    }

    [Fact]
    public async Task Publish_WithParallelStrategy_ExecutesHandlersInParallel()
    {
        using var serviceProvider = BuildServiceProvider(NotificationPublishStrategy.Parallel);

        var publisher = serviceProvider.GetRequiredService<IPublisher>();
        var concurrency = serviceProvider.GetRequiredService<NotificationConcurrencyTracker>();

        await publisher.Publish(new ParallelPingNotification(), CancellationToken.None);

        concurrency.MaxConcurrency.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Send_ExecutesPreAndPostProcessors()
    {
        using var serviceProvider = BuildServiceProvider();

        var sender = serviceProvider.GetRequiredService<ISender>();
        var trace = serviceProvider.GetRequiredService<ExecutionTrace>();

        await sender.Send(new ProcessedPingRequest(), CancellationToken.None);

        trace.Steps.ShouldContain("PreProcessor");
        trace.Steps.ShouldContain("ProcessedHandler");
        trace.Steps.ShouldContain("PostProcessor");

        trace.Steps.IndexOf("PreProcessor").ShouldBeLessThan(trace.Steps.IndexOf("ProcessedHandler"));
        trace.Steps.IndexOf("ProcessedHandler").ShouldBeLessThan(trace.Steps.IndexOf("PostProcessor"));
    }

    [Fact]
    public async Task CreateStream_HandlesRequest_WithRegisteredHandlerAndBehavior()
    {
        using var serviceProvider = BuildServiceProvider();

        var streamSender = serviceProvider.GetRequiredService<IStreamSender>();
        var trace = serviceProvider.GetRequiredService<ExecutionTrace>();

        var values = new List<int>();

        await foreach (var item in streamSender.CreateStream(new NumberStreamRequest(3), CancellationToken.None))
            values.Add(item);

        values.ShouldBe([1, 2, 3]);
        trace.Steps.ShouldContain("StreamBehavior:Before");
        trace.Steps.ShouldContain("StreamHandler");
        trace.Steps.ShouldContain("StreamBehavior:After");
    }

    private static ServiceProvider BuildServiceProvider(NotificationPublishStrategy publishStrategy = NotificationPublishStrategy.Sequential)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExecutionTrace>();
        services.AddSingleton<NotificationTracker>();
        services.AddSingleton<NotificationConcurrencyTracker>();
        services.AddMediator(configure =>
            configure
                .RegisterServicesFromAssemblies(typeof(MediatorRuntimeTests).Assembly)
                .UseNotificationPublishStrategy(publishStrategy));

        return services.BuildServiceProvider();
    }

    private sealed class ExecutionTrace
    {
        public List<string> Steps { get; } = [];
    }

    private sealed class NotificationTracker
    {
        public List<string> HandledBy { get; } = [];
    }

    private sealed class NotificationConcurrencyTracker
    {
        private int _active;

        public int MaxConcurrency { get; private set; }

        public async Task TrackAsync(Func<Task> action)
        {
            var current = Interlocked.Increment(ref _active);
            MaxConcurrency = Math.Max(MaxConcurrency, current);

            try
            {
                await action();
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class PingRequest : IRequest<Result>;

    private sealed class PingRequestHandler(ExecutionTrace trace) : IRequestHandler<PingRequest, Result>
    {
        public Task<Result> Handle(PingRequest request, CancellationToken cancellationToken)
        {
            trace.Steps.Add("Handler");
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class ProcessedPingRequest : IRequest<Result>;

    private sealed class ProcessedPingRequestHandler(ExecutionTrace trace) : IRequestHandler<ProcessedPingRequest, Result>
    {
        public Task<Result> Handle(ProcessedPingRequest request, CancellationToken cancellationToken)
        {
            trace.Steps.Add("ProcessedHandler");
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class ProcessedPingRequestPreProcessor(ExecutionTrace trace) : IRequestPreProcessor<ProcessedPingRequest>
    {
        public Task Process(ProcessedPingRequest request, CancellationToken cancellationToken)
        {
            trace.Steps.Add("PreProcessor");
            return Task.CompletedTask;
        }
    }

    private sealed class ProcessedPingRequestPostProcessor(ExecutionTrace trace) : IRequestPostProcessor<ProcessedPingRequest, Result>
    {
        public Task Process(ProcessedPingRequest request, Result response, CancellationToken cancellationToken)
        {
            trace.Steps.Add("PostProcessor");
            return Task.CompletedTask;
        }
    }

    private sealed class FirstBehavior(ExecutionTrace trace)
        : IPipelineBehavior<PingRequest, Result>
    {
        public async Task<Result> Handle(PingRequest request, RequestHandlerDelegate<Result> next, CancellationToken cancellationToken)
        {
            trace.Steps.Add("First:Before");
            var response = await next(cancellationToken);
            trace.Steps.Add("First:After");
            return response;
        }
    }

    private sealed class SecondBehavior(ExecutionTrace trace)
        : IPipelineBehavior<PingRequest, Result>
    {
        public async Task<Result> Handle(PingRequest request, RequestHandlerDelegate<Result> next, CancellationToken cancellationToken)
        {
            trace.Steps.Add("Second:Before");
            var response = await next(cancellationToken);
            trace.Steps.Add("Second:After");
            return response;
        }
    }

    private sealed class PingNotification : INotification;

    private sealed class ParallelPingNotification : INotification;

    private sealed class FirstNotificationHandler(NotificationTracker tracker) : INotificationHandler<PingNotification>
    {
        public Task Handle(PingNotification notification, CancellationToken cancellationToken)
        {
            tracker.HandledBy.Add("FirstNotificationHandler");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondNotificationHandler(NotificationTracker tracker) : INotificationHandler<PingNotification>
    {
        public Task Handle(PingNotification notification, CancellationToken cancellationToken)
        {
            tracker.HandledBy.Add("SecondNotificationHandler");
            return Task.CompletedTask;
        }
    }

    private sealed class ParallelFirstNotificationHandler(NotificationConcurrencyTracker tracker) : INotificationHandler<ParallelPingNotification>
    {
        public Task Handle(ParallelPingNotification notification, CancellationToken cancellationToken)
            => tracker.TrackAsync(async () => await Task.Delay(60, cancellationToken));
    }

    private sealed class ParallelSecondNotificationHandler(NotificationConcurrencyTracker tracker) : INotificationHandler<ParallelPingNotification>
    {
        public Task Handle(ParallelPingNotification notification, CancellationToken cancellationToken)
            => tracker.TrackAsync(async () => await Task.Delay(60, cancellationToken));
    }

    private sealed record NumberStreamRequest(int Count) : IStreamRequest<int>;

    private sealed class NumberStreamRequestHandler(ExecutionTrace trace) : IStreamRequestHandler<NumberStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(NumberStreamRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            trace.Steps.Add("StreamHandler");

            for (var i = 1; i <= request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
    }

    private sealed class NumberStreamBehavior(ExecutionTrace trace) : IStreamPipelineBehavior<NumberStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(
            NumberStreamRequest request,
            StreamHandlerDelegate<int> next,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            trace.Steps.Add("StreamBehavior:Before");

            await foreach (var item in next(cancellationToken).WithCancellation(cancellationToken))
                yield return item;

            trace.Steps.Add("StreamBehavior:After");
        }
    }
}