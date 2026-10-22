# Resrcify.SharedKernel.MessageBus.Testing

A test harness for services built on `Resrcify.SharedKernel.MessageBus`: no RabbitMQ, no polling loops.

- **One switch.** `AddMessageBusTestHarness()` after `AddMessageBus(...)` moves every bus of the service (its own, the
  scatter-gather reply bus, each rate-limited queue's) to an in-memory network, whatever transport it was given.
- **Recordings you can await.** `Published`, `Sent`, `Consumed`, `Faulted` (each failed delivery, retries included) and
  `DeadLettered` (given up on).
- **Wait until everything is done.** `WaitUntilIdleAsync()` returns once no bus is handling a message and no queue
  holds one, including whatever the handlers sent in turn.
- **Stand-ins for other services.** `network.StartResponderAsync<TRequest, TResponse>(...)` answers another service's
  requests through the real queue and reply path.
- **Fault injection.** `FailNext<TMessage>(deliveries)` and `FailStart(queue)`.
- **Time.** Register a `FakeTimeProvider` and the buses' deadlines, retries and backoff follow it.
- **A scripted `IScatterGatherClient`.** `ScatterGatherClientStandIn` answers each item as the test scripts it, on the
  test's clock, holds calls until the test lets them go, and records every call.

## In a WebApplicationFactory

```csharp
var network = new MessageBusTestNetwork();
var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
    web.ConfigureTestServices(services => services.AddMessageBusTestHarness(network)));
var harness = factory.Services.GetRequiredService<MessageBusTestHarness>();

await client.PostAsJsonAsync("/shards", new CreateShardRequest("Main"));

var created = await harness.Published.WaitForAsync<ShardCreated>(e => e.Name == "Main");
```

`WaitForAsync` returns at once when a matching message is already recorded, and otherwise waits for one; on a
timeout it throws, listing what was recorded. `Of<T>()` reads what is there now.

## Several services

Give each service's host the same `MessageBusTestNetwork`, as they would share RabbitMQ. A service that isn't part of
the test can be stood in for:

```csharp
await using var players = await network.StartResponderAsync<GetPlayer, PlayerProfile>(
    (request, _) => Task.FromResult(Result.Success(new PlayerProfile(request.AllyCode, "Han"))),
    queue: "swgohapi.player");
```

Pass `configure` to give the messages the wire names the real service uses (`bus => bus.AddMessage<GetPlayer>(...)`).
`players.Harness` shows what the stand-in consumed.

## On a real broker

`PublishedEventListener.StartAsync(connection, listen => listen.Listen<ShardCreated>())` subscribes to a service's
integration events on RabbitMQ as another service would (an input queue of its own, bound to each event's topic) and
records them in `Received`, a `MessageRecording` like the harness'. Start it before the publish; dispose it to
unsubscribe. See the IntegrationTesting README, whose `ServiceHostFixture` also runs a service on this harness
(`UsesMessageBusTestHarness`) and whose RabbitMQ fixture gives the `Connection`.

## End to end with the outbox

The buses and the outbox are drained separately: an outbox handler may publish, and a bus handler may save to the
outbox. Alternate the two until both are done:

```csharp
await services.GetRequiredService<OutboxWakeUp<AppDbContext>>().DrainAsync();
await harness.WaitUntilIdleAsync();
```

## Failures

```csharp
harness.FailNext<PayoutRotated>(deliveries: 2);           // two failed deliveries, then the retry goes through
harness.FailNext<PayoutRotated>(deliveries: 100);         // every try fails: it ends in DeadLettered
harness.FailStart("shardmanagement", starts: 1);          // the bus on that queue fails its next start
```

Error queues and messages deferred to later are not waited for by `WaitUntilIdleAsync`.

## A scripted scatter-gather client: `ScatterGatherClientStandIn`

For a test of what a service makes of a gather's every outcome, without a responder or a real timeout:

```csharp
var time = new FakeTimeProvider(TimeProvider.System.GetUtcNow());
var swgohApi = new ScatterGatherClientStandIn(time)
    .AnswerEvery<GetGuildRequest, GetGuildResponse>((guildId, _) => Profile(guildId))   // every guild found...
    .Fail<GetGuildRequest>("deleted", Error.NotFound("RECORDNOTFOUND", "No such guild."))   // ...but one (settled)
    .Fail<GetGuildRequest>("flaky", Error.ExternalFailure("SERVERUNAVAILABLE", "The game is down."))   // given up on
    .NeverAnswer<GetGuildRequest>("silent")                                              // no answer: waits out its timeout
    .ThrowOnCall(1, new InvalidOperationException("The bus failed."));                    // the second call throws

services.ReplaceScatterGatherClient(swgohApi);   // in ConfigureTestServices
```

- **Answers**, by request type and key: `Answer<TRequest, TResponse>(key, response)`; else the type's
  `AnswerEvery<TRequest, TResponse>((key, request) => response)`; else no answer. `NeverAnswerAny<TRequest>()` is an
  outage (no item of the type answers, whatever is scripted) until `ResumeAnswering<TRequest>()`.
- **Failures:** `Fail<TRequest>(key, errors)`. A failure is settled (in `SettledKeys`) unless one of its errors is
  transient, as the message bus decides: `NotFound` is settled, `ExternalFailure` isn't.
- **No answer:** a call with an item that never answers waits for its timeout on the test's clock, and ends when the
  test moves the clock past it. Move it from `OnCall` (`OnCall = call => time.Advance(call.Timeout)`, a responder that
  takes its whole timeout), or after `await swgohApi.TimeoutWaitsReachedAsync(1)`. Every other call answers at once.
- **A call that throws:** `ThrowOnNextCall(exception)` (once) or `ThrowOnCall(n, exception)` (the call numbered `n`,
  from 0, of any kind).
- **Holding:** `Hold()` keeps every call that comes waiting before it is answered (its timeout hasn't started);
  `await WaitUntilHeldAsync()` returns once one is; `Release()` lets them go (each starts its timeout then).
- **Recording:** `Calls` (and `CallsOf<TRequest>()`) lists every call as it arrived: its kind (`Request`, `Gather`,
  `Stream`), request type, keys, timeout and requests.
- **The chunked gather:** `GatherInChunksAsync` runs the message bus' own chunking on the stand-in's `GatherAsync` and
  the test's clock, so each chunk is one scripted, recorded, holdable gather, and the budget follows the fake clock.
- **`RequestAsync`** answers the item keyed `ScatterGatherClientStandIn.RequestKey` (`"request"`, as the bus' client
  keys it) or the type's `AnswerEvery`. **`StreamAsync`** yields every item that answered (a result or a failure), in
  key order, once the call has its answers (after its timeout when one never answers).

### In front of the real client

To hold a host's real scatter-gather calls (e.g. to show other work goes on while a batch waits), put the stand-in in
front of the bus' client:

```csharp
services.HoldScatterGathers(scatterGathers);   // after AddScatterGather; the real client answers

scatterGathers.Hold();
// ... start the work that gathers ...
await scatterGathers.WaitUntilHeldAsync();     // the batch reached the client and waits: its timeout hasn't started
// ... show what happens meanwhile ...
scatterGathers.Release();
```

There it records, holds and throws as told, then forwards every call to the real client, which answers it; nothing is
scripted.
