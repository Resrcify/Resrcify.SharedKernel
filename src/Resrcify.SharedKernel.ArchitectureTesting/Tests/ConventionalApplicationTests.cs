using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NetArchTest.Rules;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.ArchitectureTesting.Extensions;
using Resrcify.SharedKernel.ArchitectureTesting.Handlers;
using Resrcify.SharedKernel.ArchitectureTesting.Layering;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.Tests;

/// <summary>
/// Conventions for the Application layer. Strict per-marker pairings — no
/// flexibility within a kind:
/// <list type="bullet">
///   <item><c>IBaseCommand</c> impls end with <c>Command</c>;
///   <c>ICommandHandler&lt;&gt;</c> / <c>&lt;,&gt;</c> impls end with
///   <c>CommandHandler</c>.</item>
///   <item><c>IQuery&lt;&gt;</c> impls end with <c>Query</c>;
///   <c>IQueryHandler&lt;,&gt;</c> impls end with <c>QueryHandler</c>.</item>
///   <item>Pure <c>IRequest&lt;&gt;</c> impls (no Command/Query marker) end
///   with <c>Request</c>; pure <c>IRequestHandler&lt;,&gt;</c> impls end with
///   <c>RequestHandler</c>. Use this when a service doesn't differentiate
///   commands from queries (e.g. SwgohApi).</item>
/// </list>
///
/// The three buckets are mutually exclusive — a service that uses split
/// (Command/Query) gets nothing in the pure-IRequest bucket, and vice versa.
///
/// And in every layer, a request handler doesn't depend on <c>ISender</c> or
/// <c>IMediator</c> (see <see cref="Handlers_Should_NotDependOnSenderOrMediator"/>):
/// only entry points send requests.
///
/// Every test is <c>virtual</c>. Consumers can override and skip when they
/// genuinely need to:
/// <code>
/// [SkippableFact]
/// public override void Commands_Should_HaveCommandPostfix()
///     =&gt; Skip.If(true, "Reason here.");
/// </code>
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public abstract bases via inheritance.")]
public abstract class ConventionalApplicationTests : BaseArchitectureTest
{
    private const string Layer = Layers.Application;

    /// <summary>Checks the layer assemblies found by naming convention (see <see cref="LayerAssemblyResolver"/>).</summary>
    protected ConventionalApplicationTests()
    {
    }

    /// <summary>
    /// Checks the given layer assemblies (layer name → assembly) instead of the ones found by naming convention.
    /// </summary>
    protected ConventionalApplicationTests(IReadOnlyDictionary<string, Assembly> layerAssemblies)
        : base(layerAssemblies)
    {
    }

    // ─────────────────────────── Commands ───────────────────────────

    [SkippableFact]
    public virtual void Commands_Should_HaveCommandPostfix()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IBaseCommand))
            .And()
            .AreNotAbstract()
            .Should()
            .HaveNameEndingWith("Command", StringComparison.Ordinal)
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Commands_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IBaseCommand))
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Commands_Should_HaveResponsePostfix()
    {
        SkipIfNoAssembly(Layer);

        var commands = Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(ICommand<>))
            .And()
            .AreNotAbstract()
            .GetTypes();

        AssertResponseGenericArgEndsWithResponse(commands, typeof(ICommand<>));
    }

    // ─────────────────────────── Queries ────────────────────────────

    [SkippableFact]
    public virtual void Queries_Should_HaveQueryPostfix()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .And()
            .AreNotAbstract()
            .Should()
            .HaveNameEndingWith("Query", StringComparison.Ordinal)
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Queries_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Queries_Should_HaveResponsePostfix()
    {
        SkipIfNoAssembly(Layer);

        var queries = Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .And()
            .AreNotAbstract()
            .GetTypes();

        AssertResponseGenericArgEndsWithResponse(queries, typeof(IQuery<>));
    }

    // ─────────────────────── Command Handlers ───────────────────────

    [SkippableFact]
    public virtual void CommandHandlers_Should_HaveCommandHandlerPostfix()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .And()
            .AreNotAbstract()
            .Should()
            .HaveNameEndingWith("CommandHandler", StringComparison.Ordinal)
            .Evaluate();
    }

    [SkippableFact]
    public virtual void CommandHandlers_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .Evaluate();
    }

    [SkippableFact]
    public virtual void CommandHandlers_Should_ReturnTaskOfResult()
    {
        SkipIfNoAssembly(Layer);

        var handlers = Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .And()
            .AreNotAbstract()
            .GetTypes();

        AssertHandlersReturnTaskOfResult(handlers);
    }

    // ──────────────────────── Query Handlers ────────────────────────

    [SkippableFact]
    public virtual void QueryHandlers_Should_HaveQueryHandlerPostfix()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .And()
            .AreNotAbstract()
            .Should()
            .HaveNameEndingWith("QueryHandler", StringComparison.Ordinal)
            .Evaluate();
    }

    [SkippableFact]
    public virtual void QueryHandlers_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .Evaluate();
    }

    [SkippableFact]
    public virtual void QueryHandlers_Should_ReturnTaskOfResult()
    {
        SkipIfNoAssembly(Layer);

        var handlers = Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .And()
            .AreNotAbstract()
            .GetTypes();

        AssertHandlersReturnTaskOfResult(handlers);
    }

    // ──────────────── Pure IRequest / IRequestHandler ───────────────
    // Catches types using IRequest<>/IRequestHandler<,> directly without
    // going through ICommand/IQuery (e.g. SwgohApi). The Command/Query
    // buckets above remain strict; this bucket fills the gap for services
    // that don't differentiate. Filtering happens in C# (Linq) since
    // NetArchTest's chained And/Or operates on the same assertion target.

    [SkippableFact]
    public virtual void Requests_Should_HaveRequestPostfix()
    {
        SkipIfNoAssembly(Layer);

        var requests = GetPureRequestTypes();
        var failing = requests
            .Where(t => !t.Name.EndsWith("Request", StringComparison.Ordinal))
            .ToList();

        failing.ShouldBeEmpty();
    }

    [SkippableFact]
    public virtual void Requests_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        var requests = GetPureRequestTypes();
        var failing = requests.Where(t => !t.IsSealed).ToList();

        failing.ShouldBeEmpty();
    }

    [SkippableFact]
    public virtual void RequestHandlers_Should_HaveRequestHandlerPostfix()
    {
        SkipIfNoAssembly(Layer);

        var handlers = GetPureRequestHandlerTypes();
        var failing = handlers
            .Where(t => !t.Name.EndsWith("RequestHandler", StringComparison.Ordinal))
            .ToList();

        failing.ShouldBeEmpty();
    }

    [SkippableFact]
    public virtual void RequestHandlers_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        var handlers = GetPureRequestHandlerTypes();
        var failing = handlers.Where(t => !t.IsSealed).ToList();

        failing.ShouldBeEmpty();
    }

    [SkippableFact]
    public virtual void RequestHandlers_Should_ReturnTaskOfResult()
    {
        SkipIfNoAssembly(Layer);

        var handlers = GetPureRequestHandlerTypes();
        AssertHandlersReturnTaskOfResult(handlers);
    }

    // ───────────────────── Handlers don't send ──────────────────────

    /// <summary>
    /// A mediator request handler (<c>ICommandHandler&lt;&gt;</c>, <c>ICommandHandler&lt;,&gt;</c>,
    /// <c>IQueryHandler&lt;,&gt;</c>, <c>IRequestHandler&lt;,&gt;</c>, <c>IValueTaskRequestHandler&lt;,&gt;</c>,
    /// <c>IStreamRequestHandler&lt;,&gt;</c>, and the scatter-gather request handlers, which are sent like any
    /// request) doesn't depend on <see cref="ISender"/> or <see cref="IMediator"/>: no constructor parameter, field
    /// or property of either (or of anything that is one, also wrapped, like <c>Lazy&lt;ISender&gt;</c>). A request
    /// sent from inside a handler hides a workflow in the pipeline: it runs the behaviors again (validation, a second
    /// unit of work, …) inside the first request's. Only the entry points send: endpoints, Quartz command jobs, bus
    /// consumers, UI handlers.
    /// </summary>
    /// <remarks>
    /// <see cref="IPublisher"/> is allowed: publishing a notification is not sending a request. Integration event
    /// handlers, scatter-gather event handlers, request responders and pipeline behaviors aren't request handlers
    /// (they are entry points, or the pipeline itself), so they may send. Every layer is checked, not only
    /// Application: a handler can live in Infrastructure (a scatter-gather request handler) or anywhere the mediator
    /// scans.
    /// </remarks>
    [SkippableFact]
    public virtual void Handlers_Should_NotDependOnSenderOrMediator()
    {
        Skip.If(LayerAssemblies.Count == 0);

        var failing = new List<string>();
        foreach (var (layer, assembly) in LayerAssemblies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var handlers = Types
                .InAssembly(assembly)
                .That()
                .AreClasses()
                .GetTypes()
                .Where(MediatorRequestHandlers.IsRequestHandler);

            foreach (var handler in handlers)
            {
                failing.AddRange(MediatorRequestHandlers
                    .SenderDependencies(handler)
                    .Select(dependency => $"{layer}: {handler.FullName} → {dependency}"));
            }
        }

        failing.ShouldBeEmpty();
    }

    private List<Type> GetPureRequestTypes()
        => Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IRequest<>))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .Where(t => !ImplementsAnyOf(t, typeof(IBaseCommand), typeof(IQuery<>)))
            .ToList();

    private List<Type> GetPureRequestHandlerTypes()
        => Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .Where(t => !ImplementsAnyOf(
                t,
                typeof(ICommandHandler<>),
                typeof(ICommandHandler<,>),
                typeof(IQueryHandler<,>)))
            .ToList();

    private static bool ImplementsAnyOf(Type type, params Type[] openOrClosedInterfaces)
    {
        foreach (var iface in type.GetInterfaces())
        {
            foreach (var marker in openOrClosedInterfaces)
            {
                if (iface == marker)
                    return true;
                if (marker.IsGenericTypeDefinition
                    && iface.IsGenericType
                    && iface.GetGenericTypeDefinition() == marker)
                    return true;
            }
        }
        return false;
    }

    // ─────────────────────────── Helpers ────────────────────────────

    private static void AssertResponseGenericArgEndsWithResponse(
        IEnumerable<Type> types,
        Type openGenericInterface)
    {
        var failing = new List<Type>();
        foreach (var t in types)
        {
            var iface = Array.Find(
                t.GetInterfaces(),
                i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);

            if (iface is null)
            {
                failing.Add(t);
                continue;
            }

            var args = iface.GetGenericArguments();
            if (args.Length != 1)
            {
                failing.Add(t);
                continue;
            }

            if (!args[0].Name.EndsWith("Response", StringComparison.Ordinal))
                failing.Add(t);
        }

        failing.ShouldBeEmpty();
    }

    private static void AssertHandlersReturnTaskOfResult(IEnumerable<Type> handlers)
    {
        var failing = new List<Type>();
        foreach (var h in handlers)
        {
            var handle = h.GetMethod("Handle");
            if (handle is null) { failing.Add(h); continue; }

            var rt = handle.ReturnType;
            var name = rt.Name;
            if (name != typeof(Task<>).Name &&
                name != nameof(Task) &&
                name != nameof(Result) &&
                name != typeof(Result<>).Name)
            {
                failing.Add(h);
                continue;
            }

            if (rt.IsGenericType)
            {
                foreach (var arg in rt.GetGenericArguments())
                {
                    if (arg.Name != nameof(Result) && arg.Name != typeof(Result<>).Name)
                    {
                        failing.Add(h);
                        break;
                    }
                }
            }
        }

        failing.ShouldBeEmpty();
    }
}
