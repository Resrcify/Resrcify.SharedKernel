using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

/// <summary><c>ForwardEvent</c>: an integration event handed to the mediator, as a notification or as a command.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class EventForwardersTests
{
    [Fact]
    public async Task ForwardEvent_ShouldPublishTheNotification_WhenTheEventArrives()
    {
        var (log, subscriber, publisher) = await StartAsync(bus => bus.ForwardEvent<RankMoved>(moved => new RankMovedNotification(moved.Player)));
        using (subscriber)
        using (publisher)
        {
            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new RankMoved("Han"));

            await InMemoryServices.WaitUntilAsync(() => !log.Notified.IsEmpty);
            log.Notified.ShouldBe(["Han"]);
        }
    }

    [Fact]
    public async Task ForwardEvent_ShouldSendTheCommand_WhenTheEventArrives()
    {
        var (log, subscriber, publisher) = await StartAsync(bus => bus.ForwardEvent<RankMoved>(moved => new SyncRankCommand(moved.Player)));
        using (subscriber)
        using (publisher)
        {
            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new RankMoved("Leia"));

            await InMemoryServices.WaitUntilAsync(() => !log.Commanded.IsEmpty);
            log.Commanded.ShouldBe(["Leia"]);
        }
    }

    [Fact]
    public async Task ForwardEvent_ShouldNotRetryTheCommand_WhenItFailsBecauseOfTheEvent()
    {
        using var metrics = new MetricsCapture();
        var (log, subscriber, publisher) = await StartAsync(bus => bus.ForwardEvent<RankMoved>(moved => new SyncRankCommand(moved.Player)));
        using (subscriber)
        using (publisher)
        {
            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new RankMoved(SyncRankCommandHandler.UnknownPlayer));

            await InMemoryServices.WaitUntilAsync(() => metrics.Count("messagebus.events.handled event=RankMoved outcome=rejected") == 1);
            await Task.Delay(TimeSpan.FromSeconds(1));
            log.CommandAttempts.ShouldBe(1);
        }
    }

    [Fact]
    public async Task ForwardEvent_ShouldSendTheCommand_WhenItReturnsAValue()
    {
        var (log, subscriber, publisher) = await StartAsync(bus => bus.ForwardEvent<RankMoved>(moved => new CountRankCommand(moved.Player)));
        using (subscriber)
        using (publisher)
        {
            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new RankMoved("Chewie"));

            await InMemoryServices.WaitUntilAsync(() => !log.Counted.IsEmpty);
            log.Counted.ShouldBe(["Chewie"]);
        }
    }

    private static async Task<(ForwardLog Log, IHost Subscriber, IHost Publisher)> StartAsync(Action<MessageBusBuilder> forward)
    {
        var network = new InMemNetwork();
        var log = new ForwardLog();
        var subscriber = await InMemoryServices.StartAsync(
            network,
            bus => forward(bus.WithInputQueue($"subscriber-{Guid.NewGuid():N}")),
            services => services
                .AddSingleton(log)
                .AddMediator(config => config.RegisterServicesFromAssemblies(typeof(EventForwardersTests).Assembly)));
        var publisher = await InMemoryServices.StartAsync(network, _ => { });
        return (log, subscriber, publisher);
    }

    internal sealed record RankMoved(string Player);

    internal sealed record RankMovedNotification(string Player) : INotification;

    internal sealed record SyncRankCommand(string Player) : ICommand;

    internal sealed record CountRankCommand(string Player) : ICommand<int>;

    internal sealed class ForwardLog
    {
        private int _commandAttempts;

        public ConcurrentQueue<string> Notified { get; } = new();

        public ConcurrentQueue<string> Commanded { get; } = new();

        public ConcurrentQueue<string> Counted { get; } = new();

        public int CommandAttempts => Volatile.Read(ref _commandAttempts);

        public void RecordCommandAttempt()
            => Interlocked.Increment(ref _commandAttempts);
    }

    internal sealed class RankMovedNotificationHandler(ForwardLog log) : INotificationHandler<RankMovedNotification>
    {
        public Task Handle(RankMovedNotification notification, CancellationToken cancellationToken)
        {
            log.Notified.Enqueue(notification.Player);
            return Task.CompletedTask;
        }
    }

    internal sealed class SyncRankCommandHandler(ForwardLog log) : ICommandHandler<SyncRankCommand>
    {
        public const string UnknownPlayer = "nobody";

        public Task<Result> Handle(SyncRankCommand request, CancellationToken cancellationToken)
        {
            log.RecordCommandAttempt();
            if (request.Player == UnknownPlayer)
                return Task.FromResult(Result.Failure(Error.NotFound("Player.NotFound", "No such player.")));
            log.Commanded.Enqueue(request.Player);
            return Task.FromResult(Result.Success());
        }
    }

    internal sealed class CountRankCommandHandler(ForwardLog log) : ICommandHandler<CountRankCommand, int>
    {
        public Task<Result<int>> Handle(CountRankCommand request, CancellationToken cancellationToken)
        {
            log.Counted.Enqueue(request.Player);
            return Task.FromResult(Result.Success(1));
        }
    }
}
