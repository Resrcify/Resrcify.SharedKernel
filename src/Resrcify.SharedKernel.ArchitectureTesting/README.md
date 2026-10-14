# Resrcify.SharedKernel.ArchitectureTesting

Architecture tests for a layered Clean Architecture service, ready to inherit. Subclass a base in the service's
architecture-test project and xUnit runs its tests there; no rules to write.

## What you get

| Base class | Checks |
|---|---|
| `ConventionalLayerDependencyTests` | Dependencies point inward: a layer never depends on a higher tier or on a peer (Domain 0, Application 1, Persistence 2, Infrastructure and Presentation 3, Web 4), Web never reaches Domain and Presentation never reaches Persistence. Also which SharedKernel packages each layer may use (a custom layer: what the layers at its tier may use). |
| `ConventionalDomainTests` | Domain events are sealed and end with `Event`; entities have a private constructor. |
| `ConventionalApplicationTests` | Commands, queries, requests and their handlers are sealed and named by convention (`…Command`, `…CommandHandler`, `…Query`, `…QueryHandler`, `…Request`, `…RequestHandler`); handlers return `Task<Result>`. |
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
`BaseArchitectureTest`).

## Sample project

`samples/Resrcify.SharedKernel.WebApiExample/tests/Resrcify.SharedKernel.WebApiExample.ArchitectureTests` in the
repository.
