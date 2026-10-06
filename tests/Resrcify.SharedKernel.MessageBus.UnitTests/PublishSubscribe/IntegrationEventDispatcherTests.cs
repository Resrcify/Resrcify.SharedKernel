using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Caching.Primitives;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

/// <summary>
/// Duplicates: an outbox message that is retried publishes its events again, under the same message IDs, and a
/// subscriber that skips duplicates handles each one once.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class IntegrationEventDispatcherTests
{
    [Fact]
    public async Task Handle_ShouldHandleTheEventOnce_WhenARetriedOutboxMessagePublishesItAgain()
    {
        var log = new PayoutLog();
        using var metrics = new MetricsCapture();
        var (subscriber, publisher) = await StartAsync(log, skipDuplicates: true);
        using (subscriber)
        using (publisher)
        {
            var outboxMessageId = Guid.NewGuid();
            await PublishAsOutboxMessageAsync(publisher, outboxMessageId, new PayoutRotated("shard-1", 1));
            await InMemoryServices.WaitUntilAsync(() => !log.Handled.IsEmpty);
            await PublishAsOutboxMessageAsync(publisher, outboxMessageId, new PayoutRotated("shard-1", 1));   // the retry

            await InMemoryServices.WaitUntilAsync(() => log.Attempts >= 1);
            await InMemoryServices.WaitUntilAsync(() => metrics.Count("messagebus.events.handled event=PayoutRotated outcome=duplicate") >= 1);

            log.Handled.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Handle_ShouldHandleTheEventOnce_WhenACopyArrivesWhileItIsBeingHandled()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new PayoutLog { Hold = hold.Task };
        using var metrics = new MetricsCapture();
        // A cache that lets every claim through: only the instance's own in-flight check can skip the copy.
        var (subscriber, publisher) = await StartAsync(log, skipDuplicates: true, new ClaimsEveryKeyCache());
        using (subscriber)
        using (publisher)
        {
            var outboxMessageId = Guid.NewGuid();
            await PublishAsOutboxMessageAsync(publisher, outboxMessageId, new PayoutRotated("shard-1", 1));
            await PublishAsOutboxMessageAsync(publisher, outboxMessageId, new PayoutRotated("shard-1", 1));   // the retry

            await InMemoryServices.WaitUntilAsync(() => metrics.Count("messagebus.events.handled event=PayoutRotated outcome=duplicate") >= 1);
            hold.SetResult();
            await InMemoryServices.WaitUntilAsync(() => metrics.Count("messagebus.events.handled event=PayoutRotated outcome=success") >= 1);

            log.Handled.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Handle_ShouldHandleTheEventTwice_WhenTheServiceDoesNotSkipDuplicates()
    {
        var log = new PayoutLog();
        var (subscriber, publisher) = await StartAsync(log, skipDuplicates: false);
        using (subscriber)
        using (publisher)
        {
            var outboxMessageId = Guid.NewGuid();
            await PublishAsOutboxMessageAsync(publisher, outboxMessageId, new PayoutRotated("shard-1", 1));
            await PublishAsOutboxMessageAsync(publisher, outboxMessageId, new PayoutRotated("shard-1", 1));

            await InMemoryServices.WaitUntilAsync(() => log.Handled.Count >= 2);
            log.Handled.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Handle_ShouldHandleBothEvents_WhenOneOutboxMessagePublishesTwoOfTheSameKind()
    {
        var log = new PayoutLog();
        var (subscriber, publisher) = await StartAsync(log, skipDuplicates: true);
        using (subscriber)
        using (publisher)
        {
            var eventBus = publisher.Services.GetRequiredService<IEventBus>();
            using (publisher.Services.GetRequiredService<FakeOutboxMessage>().Handle(Guid.NewGuid()))
            {
                await eventBus.PublishAsync(new PayoutRotated("shard-1", 1));
                await eventBus.PublishAsync(new PayoutRotated("shard-2", 2));
            }

            await InMemoryServices.WaitUntilAsync(() => log.Handled.Count >= 2);
            log.Handled.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Handle_ShouldHandleTheEventAgain_WhenTheFirstAttemptFailed()
    {
        var log = new PayoutLog { FailFirstAttempts = 1 };
        var (subscriber, publisher) = await StartAsync(log, skipDuplicates: true);
        using (subscriber)
        using (publisher)
        {
            await PublishAsOutboxMessageAsync(publisher, Guid.NewGuid(), new PayoutRotated("shard-1", 1));

            await InMemoryServices.WaitUntilAsync(() => !log.Handled.IsEmpty);
            log.Attempts.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Handle_ShouldReportANameClash_WhenTwoServicesPublishAnEventOfTheSameName()
    {
        using var metrics = new MetricsCapture();
        var network = new InMemNetwork();
        var log = new PayoutLog();
        using var subscriber = await InMemoryServices.StartAsync(
            network,
            bus => bus.WithInputQueue($"subscriber-{Guid.NewGuid():N}").AddEventHandlers(typeof(IntegrationEventDispatcherTests).Assembly),
            services => services.AddSingleton(log));
        var shard = $"shard-{Guid.NewGuid():N}";
        var sentinel = $"sentinel-{Guid.NewGuid():N}";
        using var shardInstance = await InMemoryServices.StartAsync(network, bus => bus.WithServiceName(shard));
        using var otherShardInstance = await InMemoryServices.StartAsync(network, bus => bus.WithServiceName(shard));
        using var sentinelInstance = await InMemoryServices.StartAsync(network, bus => bus.WithServiceName(sentinel));

        await shardInstance.Services.GetRequiredService<IEventBus>().PublishAsync(new PayoutRotated("shard-1", 1));
        await InMemoryServices.WaitUntilAsync(() => log.Handled.Count == 1);
        await otherShardInstance.Services.GetRequiredService<IEventBus>().PublishAsync(new PayoutRotated("shard-1", 2));
        await InMemoryServices.WaitUntilAsync(() => log.Handled.Count == 2);
        metrics.Count($"messagebus.events.name_clashes event=PayoutRotated publishers={shard},{sentinel}").ShouldBe(0);

        await sentinelInstance.Services.GetRequiredService<IEventBus>().PublishAsync(new PayoutRotated("guild-1", 3));
        await InMemoryServices.WaitUntilAsync(() => log.Handled.Count == 3);

        metrics.Count($"messagebus.events.name_clashes event=PayoutRotated publishers={shard},{sentinel}").ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ShouldRetryInPlace_WhenAHandlerFailsInAWayAnotherTryMayFix()
    {
        var (log, _, subscriber, publisher, _) = await StartScriptedAsync();
        using (subscriber)
        using (publisher)
        {
            log.Answers.Enqueue(Result.Failure(Error.ExternalFailure("Discord.Unavailable", "Down.")));
            log.Answers.Enqueue(Result.Failure(Error.Timeout("Discord.Timeout", "Slow.")));

            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new ScriptedEvent("e1"));
            await InMemoryServices.WaitUntilAsync(() => !log.Handled.IsEmpty);

            log.Attempts.ShouldBe(3);
            log.FirstRuns.ShouldBe(1);   // the retry started at the handler that failed
        }
    }

    [Fact]
    public async Task Handle_ShouldNotRetry_WhenTheFailureIsTheEventsFault()
    {
        using var metrics = new MetricsCapture();
        var (log, network, subscriber, publisher, errorQueue) = await StartScriptedAsync();
        using (subscriber)
        using (publisher)
        {
            log.Answers.Enqueue(Result.Failure(Error.NotFound("Shard.NotFound", "No such shard.")));

            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new ScriptedEvent("e1"));
            await InMemoryServices.WaitUntilAsync(() => metrics.Count("messagebus.events.handled event=ScriptedEvent outcome=rejected") == 1);
            await Task.Delay(TimeSpan.FromSeconds(1));

            log.Attempts.ShouldBe(1);
            network.GetCount(errorQueue).ShouldBe(0);
        }
    }

    [Fact]
    public async Task Handle_ShouldMoveTheEventToTheErrorQueue_WhenEveryTryFails()
    {
        var (log, network, subscriber, publisher, errorQueue) = await StartScriptedAsync();
        using (subscriber)
        using (publisher)
        {
            for (var i = 0; i < 5; i++)
                log.Answers.Enqueue(Result.Failure(Error.Failure("Discord.Down", "Down.")));

            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new ScriptedEvent("e1"));
            await InMemoryServices.WaitUntilAsync(() => network.GetCount(errorQueue) == 1, TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromSeconds(1));

            log.Attempts.ShouldBe(5);   // tried in place, not redelivered on top
            log.Handled.ShouldBeEmpty();
        }
    }

    private static async Task<(ScriptLog Log, InMemNetwork Network, IHost Subscriber, IHost Publisher, string ErrorQueue)> StartScriptedAsync()
    {
        var network = new InMemNetwork();
        var log = new ScriptLog();
        var queue = $"subscriber-{Guid.NewGuid():N}";
        var subscriber = await InMemoryServices.StartAsync(
            network,
            bus => bus.WithInputQueue(queue).AddEventHandlers(typeof(IntegrationEventDispatcherTests).Assembly),
            services => services.AddSingleton(log));
        var publisher = await InMemoryServices.StartAsync(network, _ => { });
        return (log, network, subscriber, publisher, $"{queue}.error");
    }

    private static async Task PublishAsOutboxMessageAsync(IHost publisher, Guid outboxMessageId, PayoutRotated payout)
    {
        using (publisher.Services.GetRequiredService<FakeOutboxMessage>().Handle(outboxMessageId))
            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(payout);
    }

    private static async Task<(IHost Subscriber, IHost Publisher)> StartAsync(
        PayoutLog log,
        bool skipDuplicates,
        ICachingService? cache = null)
    {
        var network = new InMemNetwork();
        var subscriber = await InMemoryServices.StartAsync(
            network,
            bus =>
            {
                bus.WithInputQueue($"subscriber-{Guid.NewGuid():N}").AddEventHandlers(typeof(IntegrationEventDispatcherTests).Assembly);
                if (skipDuplicates)
                    bus.SkipDuplicateEvents();
            },
            services =>
            {
                services
                    .AddSingleton(log)
                    .AddDistributedMemoryCache();
                if (cache is null)
                    services.AddSingleton<ICachingService, DistributedCachingService>();
                else
                    services.AddSingleton(cache);
            });
        var outbox = new FakeOutboxMessage();
        var publisher = await InMemoryServices.StartAsync(
            network,
            _ => { },
            services => services.AddSingleton(outbox).AddSingleton<IOutboxMessageContext>(outbox));
        return (subscriber, publisher);
    }

    /// <summary>A cache whose claims always succeed, as two instances' claims can when the cache isn't atomic.</summary>
    private sealed class ClaimsEveryKeyCache
        : ICachingService
    {
        public Task<T?> GetAsync<T>(string key, JsonSerializerOptions? serializerOptions = null, CancellationToken cancellationToken = default)
            where T : class
            => Task.FromResult<T?>(null);

        public Task SetAsync<T>(
            string key,
            T value,
            DateTimeOffset? absoluteExpiration,
            TimeSpan? absoluteExpirationRelativeToNow,
            TimeSpan? slidingExpiration,
            JsonSerializerOptions? serializerOptions,
            CancellationToken cancellationToken)
            where T : class
            => Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryClaimForAsync(string key, TimeSpan expiresIn, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IEnumerable<T?>> GetBulkAsync<T>(
            IEnumerable<string> keys,
            JsonSerializerOptions? serializerOptions = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<T?>());
    }
}
