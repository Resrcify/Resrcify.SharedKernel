namespace Resrcify.SharedKernel.Abstractions.Mediator;

/// <summary>
/// A feature step: a use case's logic, kept next to its handler, in the handler's file. The handler gets the step's
/// inputs, calls it and saves; an orchestrator (a handler that runs several use cases) injects the steps it runs, never
/// their handlers.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Its dependencies come in by constructor injection (a repository, a client, its own logger); its inputs are
/// parameters, and it returns a result.</item>
/// <item>It never saves: the handler, or the orchestrator that owns the flow, does.</item>
/// <item>It never sends a request (no <see cref="ISender"/> or <see cref="IMediator"/>) and never depends on a
/// handler.</item>
/// </list>
/// The mediator's assembly scan (<c>RegisterServicesFromAssemblies</c>) registers every non-abstract class implementing
/// this as itself, scoped, internal ones included. The architecture tests (<c>ConventionalApplicationTests</c>) check that
/// a step is internal and sealed, and depends on no sender, mediator or handler.
/// </remarks>
public interface IFeatureStep;
