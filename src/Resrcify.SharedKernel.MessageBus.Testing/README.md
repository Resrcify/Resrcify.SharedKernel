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
