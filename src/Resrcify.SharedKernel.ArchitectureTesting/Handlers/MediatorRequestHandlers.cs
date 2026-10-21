using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;

namespace Resrcify.SharedKernel.ArchitectureTesting.Handlers;

/// <summary>
/// Finds the mediator request handlers of an assembly and what in them can send a request: the members typed
/// <see cref="ISender"/> or anything that is one (<see cref="IMediator"/>, the mediator itself), also wrapped
/// (<c>Lazy&lt;ISender&gt;</c>, <c>Func&lt;IMediator&gt;</c>, an array).
/// </summary>
internal static class MediatorRequestHandlers
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The handlers the mediator sends a request to. <c>ICommandHandler&lt;&gt;</c>, <c>ICommandHandler&lt;,&gt;</c>
    /// and <c>IQueryHandler&lt;,&gt;</c> are <see cref="IRequestHandler{TRequest, TResponse}"/>s; a scatter-gather
    /// request handler is sent like any request, so it is one too. Notification, integration event and
    /// scatter-gather (event) handlers, request responders and pipeline behaviors are not.
    /// </summary>
    private static readonly Type[] RequestHandlerInterfaces =
    [
        typeof(IRequestHandler<,>),
        typeof(IValueTaskRequestHandler<,>),
        typeof(IStreamRequestHandler<,>),
        typeof(IScatterGatherRequestHandler<,,,>),
        typeof(IStreamingScatterGatherRequestHandler<,,,>),
    ];

    /// <summary>Whether <paramref name="type"/> is a concrete class implementing a mediator request handler interface.</summary>
    public static bool IsRequestHandler(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsClass || type.IsAbstract)
            return false;

        return type
            .GetInterfaces()
            .Where(contract => contract.IsGenericType)
            .Select(contract => contract.GetGenericTypeDefinition())
            .Any(contract => RequestHandlerInterfaces.Contains(contract));
    }

    /// <summary>
    /// The handler's constructor parameters, fields and properties (its base classes' included) that can send a
    /// request, described for a failure message, e.g. <c>constructor parameter 'sender' (ISender)</c>. Nested types
    /// are types of their own and aren't looked at.
    /// </summary>
    public static IReadOnlyList<string> SenderDependencies(Type handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return ConstructorParameters(handler)
            .Concat(Fields(handler))
            .Concat(Properties(handler))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The constructor parameters of <paramref name="type"/> that are a request handler: a handler interface
    /// (<see cref="IRequestHandler{TRequest, TResponse}"/>, <c>ICommandHandler&lt;&gt;</c>, ...), a handler class, or an
    /// <see cref="IEnumerable{T}"/>, <see cref="Lazy{T}"/>, <see cref="Func{TResult}"/> or array of one, described for
    /// a failure message, e.g. <c>constructor parameter 'inner' (ICommandHandler&lt;PayCommand&gt;)</c>. Another
    /// generic argument (a handler's own <c>ILogger&lt;THandler&gt;</c>) isn't a dependency on it.
    /// </summary>
    public static IReadOnlyList<string> HandlerDependencies(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => IsHandler(parameter.ParameterType))
            .Select(parameter => $"constructor parameter '{parameter.Name}' ({Describe(parameter.ParameterType)})")
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static readonly Type[] HandlerWrappers =
    [
        typeof(IEnumerable<>),
        typeof(Lazy<>),
        typeof(Func<>),
    ];

    private static bool IsHandler(Type type)
    {
        if (type.HasElementType)
            return IsHandler(type.GetElementType()!);

        if (type.IsGenericType && HandlerWrappers.Contains(type.GetGenericTypeDefinition()))
            return type.GetGenericArguments().Any(IsHandler);

        return IsHandlerContract(type)
            || type.GetInterfaces().Any(IsHandlerContract);
    }

    private static bool IsHandlerContract(Type type)
        => type.IsGenericType
            && RequestHandlerInterfaces.Contains(type.GetGenericTypeDefinition());

    private static IEnumerable<string> ConstructorParameters(Type handler)
        => handler
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => CanSend(parameter.ParameterType))
            .Select(parameter => $"constructor parameter '{parameter.Name}' ({Describe(parameter.ParameterType)})");

    // A compiler-generated field (a captured primary-constructor parameter, an auto-property's backing field) is
    // already named by its constructor parameter or its property.
    private static IEnumerable<string> Fields(Type handler)
        => SelfAndBaseClasses(handler)
            .SelectMany(type => type.GetFields(DeclaredMembers))
            .Where(field => !field.Name.StartsWith('<'))
            .Where(field => CanSend(field.FieldType))
            .Select(field => $"field '{field.Name}' ({Describe(field.FieldType)})");

    private static IEnumerable<string> Properties(Type handler)
        => SelfAndBaseClasses(handler)
            .SelectMany(type => type.GetProperties(DeclaredMembers))
            .Where(property => CanSend(property.PropertyType))
            .Select(property => $"property '{property.Name}' ({Describe(property.PropertyType)})");

    private static IEnumerable<Type> SelfAndBaseClasses(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            yield return current;
    }

    private static bool CanSend(Type type)
    {
        if (type.HasElementType)
            return CanSend(type.GetElementType()!);

        if (typeof(ISender).IsAssignableFrom(type))
            return true;

        return type.IsGenericType
            && type.GetGenericArguments().Any(CanSend);
    }

    private static string Describe(Type type)
    {
        if (type.HasElementType)
            return $"{Describe(type.GetElementType()!)}{(type.IsArray ? "[]" : string.Empty)}";

        var arity = type.Name.IndexOf('`', StringComparison.Ordinal);
        if (!type.IsGenericType || arity < 0)
            return type.Name;

        var name = type.Name[..arity];
        var arguments = string.Join(", ", type.GetGenericArguments().Select(Describe));
        return $"{name}<{arguments}>";
    }
}
