# Resrcify.SharedKernel.MessageBus

`Resrcify.SharedKernel.MessageBus` is the message bus between services, on [Rebus](https://github.com/rebus-org/Rebus) and RabbitMQ.

It replaces MassTransit with:

- **Scatter-gather** (request/response):
  - **Requesting:** an `IScatterGatherHandler<TEvent, TRequest, TResponse>` handles a domain event.
    1. It returns one request per item.
    2. The requests are sent and the replies collected **in memory**.
    3. It applies everything that came back in one go.
  - **Responding:** one queue per request type, each under its own rate limit per instance.
- **Publish/subscribe** (integration events): `IEventBus.PublishAsync` in one service, an
  `IIntegrationEventHandler<TEvent>` in each service that wants it.

Moving a service from MassTransit: the migration guide in the Resrcify.SwgohApi repository
(`docs/migrating-from-masstransit.md`).

It keeps **no state and adds no tables**. Durability comes from the service's existing **outbox**. The
event commits with the aggregate that raised it, and the outbox marks it processed only after the replies
have been gathered and applied. If anything fails, the outbox retries the whole event. These events run in
the outbox's **scatter-gather lane** (see the UnitOfWork README), so a waiting batch never holds up other
outbox messages.

## Table of Contents

- [Resrcify.SharedKernel.MessageBus](#resrcifysharedkernelmessagebus)
  - [Table of Contents](#table-of-contents)
  - [Where each piece lives](#where-each-piece-lives)
  - [Install](#install)
  - [Message names](#message-names)
  - [Requesting: scatter-gather](#requesting-scatter-gather)
  - [Requesting inside a request: gather before the handler](#requesting-inside-a-request-gather-before-the-handler)
  - [Asking directly: IScatterGatherClient](#asking-directly-iscattergatherclient)
  - [Failures: the result pattern](#failures-the-result-pattern)
  - [Responding: rate-limited queues](#responding-rate-limited-queues)
  - [Publish/subscribe: integration events](#publishsubscribe-integration-events)
  - [Running on a RabbitMQ cluster](#running-on-a-rabbitmq-cluster)
  - [Running without RabbitMQ: in memory](#running-without-rabbitmq-in-memory)
  - [Checking that two copies of a message agree](#checking-that-two-copies-of-a-message-agree)
  - [Large messages: compression](#large-messages-compression)
  - [Customising: strategies](#customising-strategies)
  - [Health check](#health-check)
  - [Tracing and metrics](#tracing-and-metrics)
  - [Behaviour you can rely on](#behaviour-you-can-rely-on)

## Where each piece lives

| Layer | References | Uses |
|---|---|---|
| Domain | `Resrcify.SharedKernel.DomainDrivenDesign` | Raises the domain event (optionally `IDedupable`). |
| Application | `Resrcify.SharedKernel.Abstractions` | From `Resrcify.SharedKernel.Abstractions.MessageBus`: `IEventBus`, `IIntegrationEventHandler`, `IScatterGatherClient`, `IScatterGatherHandler`, `IScatterGatherRequestHandler`, `IStreamingScatterGatherRequestHandler`, `IGathered`, `IScatterReply` and `IRequestResponder`. |
| Infrastructure | `Resrcify.SharedKernel.MessageBus` | `AddMessageBus(...)`: transport, `AddRequest`, `AddScatterGather`, `AddRateLimitedQueue`, `AddEventHandlers`, strategies; `AddHealthChecks().AddMessageBus()`. |

Handlers only see the contracts, so the Application layer never references Rebus or this package. The
ArchitectureTesting package's `ConventionalLayerDependencyTests` enforces it (see the root README).

### Inside the package

| Folder | What |
|---|---|
| `Extensions/` | `AddMessageBus` |
| `Configuration/` | `MessageBusBuilder`, `RabbitMqConnection`, the transport (RabbitMQ or in memory), the default bus configuration strategy |
| `Broker/` | `BrokerConnectionWatcher`: restarts the buses as soon as RabbitMQ is back |
| `Abstractions/` | The strategy interfaces: `IBusConfigurationStrategy`, `IMessageSerializationStrategy`, `IRateLimiterStrategy` |
| `ScatterGather/` | The requester side: reply queue, pending batches, the mediator adapter |
| `RateLimitedQueues/` | The responder side: one bus per queue, limiters, the health gate, request deadlines |
| `PublishSubscribe/` | `IEventBus`, the event handler registration and dispatch, `SubscriptionOptions` |
| `Serialization/` | The wire JSON and its default strategy; the gzip of large bodies (`MessageCompression` and its two pipeline steps) |
| `Diagnostics/` | `MessageBusDiagnostics` (metrics), the health check, Rebus logging |

## Install

```xml
<PackageReference Include="Resrcify.SharedKernel.MessageBus" Version="<latest>" />
```

It brings Rebus, Rebus.RabbitMq, Rebus.ServiceProvider, Rebus.Diagnostics and the health-check and hosting
abstractions with it. It reaches the outbox only through its contracts (`IOutboxLaneEvent`, `IOutboxMessageContext` in
`Resrcify.SharedKernel.Abstractions`), so a service that only publishes or answers needs no outbox. Scatter-gather
handlers, which wait through the outbox, need `Resrcify.SharedKernel.UnitOfWork` (`AddOutboxProcessing`) as well.

## Message names

A message goes by its **class name** on the wire, without the namespace or assembly. Nothing has to be named: the
requester's and the responder's classes (or the publisher's and the subscribers') only need the same name, and can
live in different namespaces and packages. A request's queue is named after its request class, on both sides.

When one side's class has another name, bind it to the other side's: `AddMessage<GuildPayload>(nameof(GetGuildRequest))`
(`nameof` keeps it compiler-checked). For a breaking change, add a class with a new name (`ShardRankChangedV2`) and
run both while the other side moves. If two message types this service receives share a name, the first message fails
with an error that names both.

**Prefix event classes with their owner** (`ShardRankChanged`, `DiscordDeliveryFailed`). A request goes to its
owner's queue, but an event's name is global on the broker: two services publishing events of the same name would
reach every subscriber of either. Each event carries its publisher's service name (its input queue, or
`WithServiceName("shard")`), and a subscriber that sees one event name arrive from two services logs a warning and
counts it in `messagebus.events.name_clashes`.

## Requesting: scatter-gather

In the **requesting** service (e.g. ShardManagement). It says what it asks for; it has no say over how fast it is
answered.

```csharp
// Infrastructure: once per service.
// using Resrcify.SharedKernel.MessageBus.Extensions;     (AddMessageBus)
// using Resrcify.SharedKernel.MessageBus.Configuration;  (RabbitMqConnection, MessageBusBuilder)
services.AddMessageBus(bus => bus
    .UseRabbitMq(provider =>
    {
        var options = provider.GetRequiredService<IOptions<MessageBusOptions>>().Value;   // the service's own options
        return new RabbitMqConnection(options.Host, options.Port, options.Username, options.Password);
    })
    .AddRequest<PlayerArenaProfileRequest, PlayerArenaProfileResponse>()   // sent to the PlayerArenaProfileRequest queue
    .AddScatterGather());
```

A virtual host other than `/`, or TLS, goes on the connection (`new RabbitMqConnection(...) { VirtualHost = "svc",
UseTls = true }`), so every connection the package makes uses it: each bus', and the broker watcher's that the health
check and the restarts after an outage rely on. Certificates and other connection-factory settings go in the
configuration strategy, in both `ConfigureTransport` (the buses) and `ConfigureConnectionFactory` (the watcher).

`AddScatterGather` opens a reply queue private to this service instance (deleted when the instance goes
away), so with several instances each one receives only its own replies. Handlers need no registration:
it follows the mediator's assembly scan (whether `AddMediator` runs before or after it), wires each handler
as its event's notification handler, and puts their events in the outbox's scatter-gather lane. Handlers
outside the mediator's assemblies can be added with `AddScatterGather(extraAssembly)`.

The aggregate raises an ordinary domain event. Make it `IDedupable` to keep one in flight per key:

```csharp
public sealed record ShardRankSyncRequested(Guid Id, Guid ShardId) : DomainEvent(Id), IDedupable
{
    public string DedupKey => ShardId.ToString("N");
}
```

The handler builds the requests, then applies the gathered replies. Don't save in it; the outbox commits.

```csharp
internal sealed class SyncShardRanksHandler(IShardRepository shards)
    : IScatterGatherHandler<ShardRankSyncRequested, PlayerArenaProfileRequest, PlayerArenaProfileResponse>
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(50);

    public async Task<IReadOnlyDictionary<string, PlayerArenaProfileRequest>> ScatterAsync(
        ShardRankSyncRequested notification, CancellationToken cancellationToken)
    {
        var shard = await shards.GetShardWithMembers(notification.ShardId, cancellationToken);
        return shard!.ShardMembers.ToDictionary(m => m.Id.ToString("N"), m => new PlayerArenaProfileRequest { ... });
    }

    public async Task GatherAsync(
        ShardRankSyncRequested notification, IGathered<PlayerArenaProfileResponse> replies, CancellationToken cancellationToken)
    {
        // replies.Results (by key), replies.Failures (each item's errors), replies.UnansweredKeys,
        // replies.SettledKeys (a definite answer: a result, or a failure that is the request's fault),
        // or one item as a Result<PlayerArenaProfileResponse>: replies[key]
        var shard = await shards.GetShardWithMembers(notification.ShardId, cancellationToken);
        shard!.SyncShard(...);   // all or nothing: only when every member is in replies.SettledKeys
    }
}
```

Publishing the event straight through `IPublisher` runs the same handler in-process, without the
outbox's durability.

## Requesting inside a request: gather before the handler

When a request (a query, say) needs answers from another service before it can be handled, its handler can
scatter and gather instead. It is sent like any request (`ISender.Send`), so pipeline behaviors run first; the
bus asks, gathers, and hands the replies to the handler. `AddScatterGather` finds these handlers the same way.

```csharp
internal sealed class GetGuildRanksQueryHandler
    : IScatterGatherRequestHandler<GetGuildRanksQuery, PlayerArenaProfileRequest, PlayerArenaProfileResponse, Result<GuildRanks>>
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public Task<IReadOnlyDictionary<string, PlayerArenaProfileRequest>> ScatterAsync(
        GetGuildRanksQuery request, CancellationToken cancellationToken)
        => ...;   // one request per key

    public Task<Result<GuildRanks>> HandleAsync(
        GetGuildRanksQuery request, IGathered<PlayerArenaProfileResponse> replies, CancellationToken cancellationToken)
        => ...;   // everything that came back, or what came before the timeout
}
```

`IStreamingScatterGatherRequestHandler` gets the replies as they arrive (`IAsyncEnumerable<IScatterReply<T>>`),
so it can start on the first one, or stop waiting by stopping the enumeration (e.g. "the first 5 that answer").

The caller waits up to `Timeout`, and nothing survives a crash. For work that must, raise a domain event and use
an `IScatterGatherHandler` (through the outbox) instead.

## Asking directly: IScatterGatherClient

Code that isn't a scatter-gather handler (a query handler, a job) can ask through `IScatterGatherClient`, which
`AddScatterGather` registers. It replaces MassTransit's `IRequestClient<T>`:

```csharp
internal sealed class GuildProfileService(IScatterGatherClient client)
{
    public Task<Result<GetGuildResponse>> GetAsync(GetGuildRequest request, CancellationToken cancellationToken)
        => client.RequestAsync<GetGuildRequest, GetGuildResponse>(request, TimeSpan.FromSeconds(30), cancellationToken);
}
```

`RequestAsync` gives the response, the responder's errors, or `ScatterGather.Unanswered` (`ErrorType.Timeout`).
`GatherAsync` and `StreamAsync` do the same for a batch. Nothing survives a crash: for work that must, use an
`IScatterGatherHandler` through the outbox.

## Failures: the result pattern

A responder answers with a `Result<TResponse>` (`Resrcify.SharedKernel.Results.Primitives`), and the requester
gets each item as one:

| Responder | Requester |
|---|---|
| Returns a value | The item is in `Results`; `replies[key]` is a success. |
| Returns a failure that is the request's fault (`NotFound`, `Validation`, `Conflict`, `Unauthorized`, `Forbidden`) | Answered at once: the item is in `Failures` with the **same errors** (code, message and `ErrorType`), sent back over the bus. |
| Returns any other failure (`Failure`, `ExternalFailure`, `Timeout`, `RateLimit`) | Another try may pass: the request goes back to its queue (any instance may take it, through the rate limiter again), up to 5 tries, 0.5, 1, 2 and 4 s apart. If it still fails, the item is in `Failures` with the last try's errors. |
| Throws | Like any other failure, as `<TRequest>.ResponderFailed` (`ErrorType.Failure`). Exceptions are for bugs: return a failure instead. |
| Never answers | The item is in `UnansweredKeys`; `replies[key]` is `ScatterGather.Unanswered` (`ErrorType.Timeout`). |

A failure is an answer: it settles the item without waiting for the timeout. Type its errors for what they mean:
"no result" (e.g. the player doesn't exist) is `NotFound`, answered at once; an upstream that is down stays a
`Failure`, so it is tried again. Change the rule per queue with `queue.AnswerFailure = errors => ...`.

A requester that must not mistake an outage for "no result" uses `replies.SettledKeys`: the items with a definite
answer (a result, or a failure with no `IsTransient()` error), leaving out those never answered and those
whose responder gave up after its last try. An item that answers with a message of the wrong type fails with
`ScatterGather.UnexpectedReply`.

A responder's `HandleAsync` gets a cancellation token. Pass it on (e.g. to the HTTP call upstream):

- **It fires when the requester stops waiting** (the request's time-to-live, which is the batch's `Timeout`). The
  request is dropped unanswered instead of spending the upstream's budget on a reply nobody reads.
- **It fires when the instance stops consuming** (its health check turned unhealthy, or it shuts down). The request
  goes back to the queue, and another instance answers it at once instead of after the handler finishes.

## Responding: rate-limited queues

In the **providing** service (e.g. SwgohApi). The provider owns the queue and its limits: it knows what its
upstream allows, and every requester shares the same queue, so the limit holds however many services ask.

```csharp
services.AddHealthChecks().AddCheck<GalaxyOfHeroesServiceHealthCheck>("galaxy-of-heroes-api", tags: ["ready", "game"]);
services.AddMessageBus(bus => bus
    .UseRabbitMq(...)
    // Consumes the PlayerArenaProfileRequest queue. Gate it on a tag only the upstream's check carries.
    .AddRateLimitedQueue<PlayerArenaProfileRequest, PlayerArenaProfileResponse, PlayerArenaProfileHandler>(
        queue => { queue.PerSecond = 100; queue.Burst = 10; queue.HealthCheckTag = "game"; }));

internal sealed class PlayerArenaProfileHandler(ISender sender)
    : IRequestResponder<PlayerArenaProfileRequest, PlayerArenaProfileResponse>
{
    public async Task<Result<PlayerArenaProfileResponse>> HandleAsync(PlayerArenaProfileRequest request, CancellationToken ct)
        => ...;   // NotFound/Validation/...: answered at once; any other failure: tried again, then answered
}
```

Each `AddRateLimitedQueue` runs its own bus and token bucket, so every request type has its own limit
**per instance**. Instances compete on the same queue, so adding one adds capacity.

### Without a responder class: through the mediator

Most responders only turn the request into a mediator request and send it. That is the default:

```csharp
services.AddMessageBus(bus => bus
    .UseRabbitMq(...)
    .AddRateLimitedQueue<GetGuildRequest, GetGuildResponse>(
        request => new GetGuild(request.GuildId),          // an IRequest<Result<GetGuildResponse>>
        queue => { queue.PerSecond = 18; queue.HealthCheckTag = "game"; }));
```

The mediator's handler (with its pipeline behaviors) answers, and its result is treated like a responder's (see
[Failures: the result pattern](#failures-the-result-pattern)).

## Publish/subscribe: integration events

An integration event goes to **every service that subscribes**, and within a service to **one of its instances**.
Publisher and subscribers agree on the event's class name, not its namespace, so each side keeps its own class.

**Publishing.** `AddMessageBus` registers `IEventBus` (no input queue needed). Publish from a domain event handler, so
the outbox runs it: the change and its domain event commit together, and the publish is retried until it succeeds.

```csharp
services.AddMessageBus(bus => bus.UseRabbitMq(...));   // nothing to register for publishing

internal sealed class RankChangedEventHandler(IEventBus eventBus) : IDomainEventHandler<RankChangedEvent>
{
    public Task Handle(RankChangedEvent notification, CancellationToken cancellationToken)
        => eventBus.PublishAsync(new RankChangedEventMessage(...), cancellationToken);
}
```

**Subscribing.** Give the service an input queue (its instances share it) and `AddEventHandlers()`. Every
`IIntegrationEventHandler<TEvent>` in the assemblies the mediator scans is found, and the input queue subscribes to
its event when the service starts.

```csharp
services.AddMessageBus(bus => bus
    .UseRabbitMq(...)
    .WithInputQueue("discordmanagement")
    .AddEventHandlers()   // subscribes to every event with an IIntegrationEventHandler
    // Optional: one at a time per shard, like MassTransit's UsePartitioner(16, e => e.ShardId).
    // In publish order per shard; the service's input queue then has a single active consumer (one instance
    // consumes, the others stand by). See HandleInPartitions for retries and the error queue.
    .ConfigureSubscription<PayoutRotatedEventMessage>(options => options.HandleInPartitions(e => e.ShardId)));

internal sealed class RankChangedEventMessageHandler(ISender sender) : IIntegrationEventHandler<RankChangedEventMessage>
{
    public Task<Result> HandleAsync(RankChangedEventMessage integrationEvent, CancellationToken cancellationToken)
        => sender.Send(new NotifyRankChangedCommand(...), cancellationToken);
}
```

**Forwarding to the mediator.** A handler that only turns the event into a notification or a command needn't be
written:

```csharp
services.AddMessageBus(bus => bus
    .WithInputQueue("discordmanagement")
    .ForwardEvent<RankChangedEventMessage>(message => new RankChangedEvent(message))          // a notification
    .ForwardEvent<ShardDeliveryDisabledEventMessage>(message => new DisableDeliveryCommand(message.ShardId)));  // a command
```

**Failures, without exceptions.** A handler (or a forwarded command) returns a `Result`, and the bus reads its errors:

| The handler returns | The bus |
|---|---|
| a success | is done |
| a failure that is the event's fault (every error `NotFound`, `Validation`, `Conflict`, `Unauthorized` or `Forbidden`) | logs it and doesn't retry (`outcome=rejected`): another try would fail the same way |
| any other failure (`Failure`, `ExternalFailure`, `Timeout`, `RateLimit`), or a concurrency conflict | tries the handler again in place (5 tries, 0.5, 1, 2 and 4 s apart; more tries wait 4 s each), each try in a DI scope and a Rebus transaction of its own (what a failed try changed or published is dropped), then moves the event to the service's own `<input queue>.error` (`outcome=error`) |

An exception is a bug, not an answer, but it's caught and handled like the last row; only a shutdown leaves the event
for the next instance. A retry starts at the handler that failed, so the ones before it don't run twice. The number of
tries follows the bus' retry strategy (`options.RetryStrategy(maxDeliveryAttempts: ...)` in a configuration strategy).

- **Delivery is at-least-once.** A redelivered event, or one published again by a retried outbox message, can arrive
  twice. `SkipDuplicateEvents()` handles each one once: an event published from the outbox gets a message ID derived from
  the outbox message (`IOutboxMessageContext`), the same on every retry, and the subscriber remembers the IDs it handled in
  its `ICachingService` (3 hours by default: an outbox retry or a redelivery comes within minutes; each entry expires
  then). Over an in-memory cache each instance remembers its own; over Redis, the
  whole service does. Without it, handling an event twice must be harmless.
  The ID is claimed before the handlers run, in one step (`IClaimStore.TryClaimForAsync`: the registered claim store,
  or the registered `ICachingService` when it is one, as `DistributedCachingService` is), and a copy that arrives
  while the event is being handled on the same instance is skipped without asking the cache, so two copies arriving
  together aren't both handled. Across instances that holds as far as the cache claims atomically: Redis with
  `SET NX` does, `DistributedCachingService` only within one process. The claim is released when the event goes to the
  error queue or the handling stops (a shutdown), so its next delivery is handled rather than skipped.
- `HandleOneAtATime()` handles an event type one at a time on each instance (MassTransit's `ConcurrentMessageLimit = 1`).
- **A subscription is a binding on the broker** and outlives the service. When you delete a handler, keep
  `RemoveSubscription<TEvent>()` (or `RemoveSubscription("TEvent")` once the class is gone) for a release: it unbinds
  at start-up. Until then its events fail ("no message type here is called …"), count as `messagebus.messages.unknown`,
  and end up in the error queue.

## Running on a RabbitMQ cluster

With more than one node, make the queues quorum queues and lift their delivery limit, on the broker: quorum as the
vhost's default queue type, and a policy `"delivery-limit": -1` for quorum queues. Classic queues aren't replicated,
and a quorum queue otherwise drops a message the bus handed back 20 times (a service stopping, a health gate closing).
The commands, and how to move existing queues: [docs/OPERATIONS.md](../../docs/OPERATIONS.md). Point the services at
the cluster's one address (in Kubernetes, its Service), not at a list of nodes.

## Running without RabbitMQ: in memory

`UseInMemory()` runs every bus of the service in memory: scatter-gather, rate-limited queues and publish/subscribe
all work within the process. Use it for local runs without a broker and in tests; messages don't survive a restart.

```csharp
services.AddMessageBus(bus =>
{
    if (options.Host is { Length: > 0 })
        bus.UseRabbitMq(new RabbitMqConnection(options.Host, options.Port, options.Username, options.Password));
    else
        bus.UseInMemory();
    ...
});
```

To test several services together, give their hosts the same network: `var network = new InMemNetwork();` then
`bus.UseInMemory(network)` in each.

For tests, the **`Resrcify.SharedKernel.MessageBus.Testing`** package does this for a service configured for RabbitMQ
(`services.AddMessageBusTestHarness(network)`), and records what every bus published, sent, consumed and failed, waits
until all are idle, injects failures and stands in for other services' queues. See its README.

## Checking that two copies of a message agree

Each service keeps its own class for a message it shares, so nothing notices when one copy drifts (a renamed
property, a changed nested class). One line in a test does:

```csharp
[Fact]
public void SentinelArenaUnitsChanged_ShouldReadAsDiscordsCopy()
    => MessageContract.Differences<Sentinel.SentinelArenaUnitsChanged, Discord.SentinelArenaUnitsChanged>(Sample)
        .ShouldBeEmpty();
```

`Differences` writes the sample as the sender's bus does (pass the sender's serialization strategy, if it has one),
reads it as the receiver's class and lists every property dropped, read differently, or not sent. Give the sample a
value in every property, nested ones and lists included.

## Large messages: compression

Message bodies of 32 KB or more are gzipped on every bus (`CompressMessagesAbove(bytes)` changes the threshold, `null`
turns it off). Every bus reads a gzipped message, whatever its own setting. Rebus.Diagnostics' message-size metric
shows what is actually sent.

The format is Rebus' own `EnableCompression` (the body gzipped, header `rbs2-content-encoding: gzip`, same threshold
rule), so a service on plain Rebus compression reads what this bus sends and the other way round. It is faster:
it compresses at `CompressionLevel.Fastest` (Rebus: `Optimal`), and unzips into a buffer sized from the gzip trailer
instead of a growing stream (falling back to growing when the trailer can't be believed). The compressed body is
larger. For a 755 KB roster message (`MessageCompressionBenchmarks` in the PerformanceTests): zipping takes 1.8 ms
instead of 7.2 ms, unzipping 0.86 ms and 0.74 MB allocated instead of 0.82 ms and 2.7 MB, and the body is 313 KB
instead of 189 KB. A forwarded or dead-lettered message stays compressed.

## Customising: strategies

Like the outbox's `IOutboxInsertStrategy`, the bus is extended through strategy interfaces
(`Resrcify.SharedKernel.MessageBus.Abstractions`) rather than by subclassing:

| Strategy | Set with | Default | Use it for |
|---|---|---|---|
| `IBusConfigurationStrategy` | `bus.UseConfigurationStrategy(...)` | `DefaultBusConfigurationStrategy` (changes nothing) | Anything Rebus or the RabbitMQ transport allows: retries, connection-factory settings (e.g. TLS), the error queue, publisher confirms. It runs after the defaults on **every** bus: the service's own, the scatter-gather reply bus and each rate-limited queue. |
| `IRateLimiterStrategy` | `queue.RateLimiter = ...` | `TokenBucketRateLimiterStrategy` (`PerSecond`, `Burst`) | Any .NET `RateLimiter` for one queue, e.g. a sliding window or a limiter shared with an HTTP client. Return one that queues: a request waits for a lease rather than being rejected. Several queues can share one budget with `SharedRateLimiterStrategy` (e.g. `SharedRateLimiterStrategy.TokenBucket(40, 10)`): together they stay within it on each instance. |
| `IMessageSerializationStrategy` | `bus.UseSerializationStrategy(...)` | `DefaultMessageSerializationStrategy` (System.Text.Json web defaults) | The JSON of every bus, both directions: converters, naming, or members kept off the wire. The receiver's class doesn't limit what is sent, so filter sensitive data here, on the sending side. |

```csharp
internal sealed class RetryAndConfirmsStrategy : IBusConfigurationStrategy
{
    public void ConfigureTransport(RabbitMqOptionsBuilder transport)
        => transport.SetPublisherConfirms(true);

    public void ConfigureOptions(OptionsConfigurer options)
        => options.RetryStrategy(maxDeliveryAttempts: 10);
}

services.AddMessageBus(bus => bus
    .UseRabbitMq(...)
    .UseConfigurationStrategy(new RetryAndConfirmsStrategy())   // or a factory: provider => ...
    .AddRateLimitedQueue<GuildRequest, GuildResponse, GuildHandler>(
        queue => queue.RateLimiter = new SlidingWindowStrategy()));
```

A limiter is created each time a queue's bus starts (including after a health-gate restart) and
disposed when it stops.

## Health check

```csharp
services.AddHealthChecks().AddMessageBus(tags: ["ready"]);
```

(The queues' own gate is separate: every queue on a health-check tag shares one check of it, every
`HealthCheckInterval` (the shortest any of them asks for) and early after a failed request, so an upstream's check
runs once per instance, not once per queue.)

Unhealthy (or the `failureStatus` you pass) while RabbitMQ is unreachable; always healthy in memory. Its data lists
each rate-limited queue as `consuming` or `stepped aside`. A queue that stepped aside doesn't change the status: its
own health check (e.g. the upstream's) already says why. **Never give it the tag a rate-limited queue is gated on**:
the queue would stop on every broker blip, which it already recovers from by itself.

## Tracing and metrics

Every bus emits OpenTelemetry-compatible spans (Rebus.Diagnostics): a span per send and per handled message,
with the trace context carried in the message headers, so a responder's handling joins the requester's trace.
Rebus only traces a send that has a parent span, which the outbox provides: every processed outbox message
gets a span (`OutboxDiagnostics`, in UnitOfWork). One scatter-gather batch is then one trace, with each
request's send, its handling in the provider, and the reply.

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(OutboxDiagnostics.ActivitySourceName)            // Resrcify.SharedKernel.UnitOfWork.Outbox
        .AddSource(RebusDiagnosticConstants.ActivitySourceName))    // Rebus.Diagnostics
    .WithMetrics(metrics => metrics
        .AddMeter(RebusDiagnosticConstants.MeterName)               // message counts, sizes and delays
        .AddMeter(MessageBusDiagnostics.MeterName));                // Resrcify.SharedKernel.MessageBus.Diagnostics
```

Delivery is at-least-once, so a request can be answered more than once (e.g. when a responder's bus stops
after it answered but before it acknowledged). The requester takes the first answer per item and ignores the
rest; `messagebus.scatter_gather.replies` (by `outcome`: `accepted`, `duplicate`, `late`) shows how many there are.

`Rebus.OpenTelemetry`'s `AddRebusInstrumentation()` is the same `AddSource` call, so it isn't needed.

The package's own metrics (`MessageBusDiagnostics.MeterName`):

| Metric | Tags | What |
|---|---|---|
| `messagebus.scatter_gather.batch.duration` (s) | `message`, `end`: complete / timeout / stopped | Sending a batch until it ends |
| `messagebus.scatter_gather.items` | `message`, `outcome`: answered / failed / gave_up / unanswered | How each item ended: `failed` = a failure that is the request's fault (e.g. not found), `gave_up` = its responder kept failing until its last try |
| `messagebus.scatter_gather.replies` | `outcome`: accepted / duplicate / late / unreadable | Replies received (only accepted ones count) |
| `messagebus.requests.handled`, `messagebus.requests.duration` (s) | `queue`, `outcome`: success / failure / retried / gave_up / expired / cancelled | Requests on a rate-limited queue; `failure` = answered with errors that are the request's fault, `retried` = sent back to the queue for another try, `gave_up` = still failing on its last try, answered with its errors: **alert on it** |
| `messagebus.requests.dropped` | `queue` | Requests the bus itself couldn't handle (unreadable, or cancelled on every try) and dropped |
| `messagebus.rate_limiter.wait` (s) | `queue` | Time a request waited for its rate limit |
| `messagebus.queue.consuming` (gauge) | `queue` | 1 while consuming, 0 while stepped aside |
| `messagebus.events.published` | `event`, `via_outbox` | Integration events published; `via_outbox=false` marks one published outside the outbox (a command handler, a callback), which nothing retries if the publish fails |
| `messagebus.events.handled`, `messagebus.events.duration` (s) | `event`, `outcome`: success / rejected / error / duplicate | Integration events handled: `rejected` = a failure that is the event's fault (not retried), `error` = moved to the error queue after its tries, `duplicate` = skipped |
| `messagebus.messages.dead_lettered` | `queue`, `message` | Messages moved to an error queue after failing: **alert on it** |
| `messagebus.messages.unknown` | `message` | Messages of a type this service doesn't receive (e.g. a deleted handler's event) |
| `messagebus.events.name_clashes` | `event`, `publishers` | One event name arriving from two services: rename one (owner prefix) |

## Behaviour you can rely on

| Situation | What happens |
|---|---|
| The unit of work that raised the event rolls back | Nothing is sent. |
| The broker refuses a publish or send (a queue that rejects when full, a node lost mid-send) | It throws: publisher confirms are on, so an outbox retries the message rather than marking it sent. A configuration strategy can turn them off (`SetPublisherConfirms(false)`). |
| Every item answers | `GatherAsync` runs as soon as the last reply arrives. |
| Some items never answer | `GatherAsync` runs at the handler's `Timeout`, with them in `UnansweredKeys`. |
| A responder returns a failure | The item is in `Failures` with the responder's errors, without waiting for the timeout. |
| A reply arrives twice, or late | Ignored: an item settles once, and a gathered batch takes no more replies. |
| `GatherAsync` throws, or the instance stops mid-wait | The outbox retries the whole event (up to its retry limit), sending the requests again. |
| The same `IDedupable` key is raised while one is in flight | The new event is dropped (with the Postgres insert strategy and dedup index). |
| A batch is waiting | Other outbox messages are unaffected: the batch runs in its own lane. |
| A request is sent before its responder has ever started | Its queue is declared by the sender, so it waits there. |
| A request outlives its timeout | It expires on the bus (`TimeToBeReceived`) and is never handled. |
| A responder's health check turns unhealthy | Its queue's bus is shut down, which hands the messages it prefetched back to the other instances. It restarts once healthy. (Pausing the workers would strand them.) |
| A responder starts failing requests between health checks (e.g. it lost its upstream) | The first failure brings its health check forward (at most once a second), and the failed request goes back to the queue. If unhealthy, it stops consuming within about a second, and the other instances take the requests instead of their tries being used up on this one. |
| RabbitMQ restarts | The scatter-gather reply bus and every rate-limited queue restart as soon as the broker is back (about a second), instead of after Rebus' own one-minute wait. The service's own bus (events, commands) is Rebus' and resumes within that minute: nothing is lost, delivery waits. |
| A request keeps failing on a responder (5 tries) | It is answered with the last try's errors. A message the bus can't handle at all (unreadable) is logged and dropped instead, not moved to the `error` queue: the requester counts it as unanswered. |
| A request's requester stops waiting while it is handled | The handler's token is cancelled and the request is dropped unanswered. |
| A responder stops consuming mid-request (unhealthy, or shutting down) | The handler's token is cancelled and the request goes back to the queue, for another instance. |
| An integration event's handler keeps failing | After 5 attempts (or the strategy's `RetryStrategy`) it moves to the service's `<input queue>.error` queue, counted in `messagebus.messages.dead_lettered`. |
| An outbox message is retried after its handler published | The event goes out again under the same message ID; a subscriber with `SkipDuplicateEvents()` handles it once. |
| A handler is deleted but its subscription stays | Its events fail and go to the error queue (`messagebus.messages.unknown`); `RemoveSubscription` unbinds them. |
| A streamed batch's caller reads slowly | Only unread replies are held; a reply is released once read. |
| A reply can't be read (a type the requester doesn't receive) | It is dropped and counted (`messagebus.scatter_gather.replies`, outcome `unreadable`), not moved to Rebus' shared `error` queue; its item ends unanswered. |
