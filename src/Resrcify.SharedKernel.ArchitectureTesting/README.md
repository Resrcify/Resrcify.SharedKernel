# Resrcify.SharedKernel.ArchitectureTesting

Architecture tests for a layered Clean Architecture service, ready to inherit. Subclass a base in the service's
architecture-test project and xUnit runs its tests there; no rules to write.

## What you get

| Base class | Checks |
|---|---|
| `ConventionalLayerDependencyTests` | Dependencies point inward: a layer never depends on a higher tier or on a peer (Domain 0, Application 1, Persistence 2, Infrastructure and Presentation 3, Web 4), Web never reaches Domain and Presentation never reaches Persistence. Also which SharedKernel packages each layer may use (a custom layer: what the layers at its tier may use). |
| `ConventionalDomainTests` | Domain events are sealed and end with `Event`; entities have a private constructor. |
| `ConventionalApplicationTests` | Commands, queries, requests and their handlers are sealed and named by convention (`…Command`, `…CommandHandler`, `…Query`, `…QueryHandler`, `…Request`, `…RequestHandler`); handlers return `Task<Result>`. In every layer, a request handler doesn't depend on `ISender` or `IMediator` ([below](#handlers-dont-send-requests)). |
| `ConventionalPresentationTests` | Endpoints implement `IEndpoint`, are sealed, expose a single map method and go through the mediator; so do controllers. |

The service's layer assemblies are found by name: a test project called `Titan.Shard.ArchitectureTests` uses every
referenced assembly named `Titan.Shard.<Layer>`. A layer the service doesn't have is skipped.

## Install

```sh
dotnet add package Resrcify.SharedKernel.ArchitectureTesting
```

Reference each layer project from the architecture-test project, so its assembly is found.

## Use

One class per base:

```csharp
public sealed class LayerTests : ConventionalLayerDependencyTests;
public sealed class DomainTests : ConventionalDomainTests;
public sealed class ApplicationTests : ConventionalApplicationTests;
public sealed class PresentationTests : ConventionalPresentationTests;
```

Every test is `virtual`; skip one the service deliberately breaks, with the reason:

```csharp
[SkippableFact]
public override void Commands_Should_HaveCommandPostfix()
    => Skip.If(true, "This service names its commands after the RPC.");
```

Add a layer, or move one to another tier, by overriding `LayerTiers`; add rules the tiers can't express with
`ExtraForbiddenDependencies`:

```csharp
public sealed class LayerTests : ConventionalLayerDependencyTests
{
    protected override IReadOnlyDictionary<string, int> LayerTiers
        => new Dictionary<string, int>(base.LayerTiers) { ["Bot"] = 4 };

    protected override IEnumerable<(string Source, string Target)> ExtraForbiddenDependencies
        => [.. base.ExtraForbiddenDependencies, ("Bot", Layers.Domain)];
}
```

A layer added this way also gets the SharedKernel package restrictions (`SharedKernelPackageRestrictions`, which name
only the conventional layers): those of the conventional layers at its tier. The `Bot` above, at tier 4, may use what
Web may use; a layer at tier 3 is restricted like Infrastructure and Presentation together; one at a tier without a
conventional layer, like the nearest tier's (the inner one on a tie). A failure says where the rule came from
(`Bot → Repository: … (restricted like Web; …)`). Choose the layer yourself with `PackageRestrictionsLike`:

```csharp
protected override IReadOnlyDictionary<string, string> PackageRestrictionsLike
    => new Dictionary<string, string> { ["Client"] = Layers.Infrastructure };
```

A custom layer that an overridden `SharedKernelPackageRestrictions` already names is left as configured there (unless
`PackageRestrictionsLike` maps it), so an override that adds it by hand keeps working; it is no longer needed.

To check assemblies that aren't named `<Root>.<Layer>`, pass them to the base's constructor
(`ConventionalLayerDependencyTests(IReadOnlyDictionary<string, Assembly> layerAssemblies)`, also on
`ConventionalApplicationTests` and `BaseArchitectureTest`).

## Handlers don't send requests

`ConventionalApplicationTests.Handlers_Should_NotDependOnSenderOrMediator` fails for a mediator request handler that
depends on `ISender` or `IMediator`: a constructor parameter (a primary constructor's too), a field or a property, its
base classes' included, of either type, of anything that is one (the mediator itself, an interface deriving from them),
or of one wrapped (`Lazy<ISender>`, `Func<IMediator>`, an array). The failure names each, by layer:

```text
Infrastructure: Titan.Shard.Infrastructure.Sync.SyncShardCommandHandler → constructor parameter 'sender' (ISender)
```

A request handler is a type implementing `ICommandHandler<>`, `ICommandHandler<,>`, `IQueryHandler<,>`,
`IRequestHandler<,>`, `IValueTaskRequestHandler<,>` or `IStreamRequestHandler<,>`, or a scatter-gather request handler
(`IScatterGatherRequestHandler<,,,>`, `IStreamingScatterGatherRequestHandler<,,,>`), which is sent like any request.

Why: a request sent from inside a handler hides a workflow in the pipeline. The inner request runs the behaviors again
inside the outer one (logging, validation, a second unit of work and transaction, caching), its failure has to be
mapped back by hand, and the use case is no longer readable in one place. Only the entry points send: endpoints,
Quartz command jobs, bus consumers, UI handlers (a Discord command, an autocomplete). A handler that needs another use
case's work does it through that work's abstractions (a repository, a domain service, a client), or the entry point
sends both requests.

What the rule allows:

- **`IPublisher`**: publishing a notification (a domain event) isn't sending a request.
- **Integration event handlers** (`IIntegrationEventHandler<>`), scatter-gather event handlers
  (`IScatterGatherHandler<,,>`) and request responders (`IRequestResponder<,>`): they are bus consumers, entry points
  of their own, so they may send. So may notification handlers (`INotificationHandler<>`, `IDomainEventHandler<>`):
  the rule is about request handlers only.
- **Pipeline behaviors**: they are the pipeline, not a handler.
- **A nested type**: it is a type of its own. A handler isn't failed for what a type nested in it holds (and a handler
  nested in a static class is checked like any other).

Every layer assembly is checked, not only Application: a handler can live in Infrastructure (a scatter-gather request
handler) or in any assembly the mediator scans. The rule has no allow-list; a service that must keep a sending handler
for now skips the test with the reason, as above, until it is moved.

## Sample project

`samples/Resrcify.SharedKernel.WebApiExample/tests/Resrcify.SharedKernel.WebApiExample.ArchitectureTests` in the
repository.
