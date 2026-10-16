using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Resrcify.SharedKernel.Abstractions.Resilience;

/// <summary>Registers the <see cref="ITransientFailureClassifier"/> and the rules it adds to the built-in one.</summary>
public static class TransientFailureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="ITransientFailureClassifier"/> (a singleton: the built-in rule OR every
    /// registered <see cref="ITransientFailureRule"/>), unless one is registered already. The packages that classify
    /// call it themselves; a service replaces the classifier by registering its own before them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection TryAddTransientFailureClassifier(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ITransientFailureClassifier, TransientFailureClassifier>();
        return services;
    }

    /// <summary>
    /// Adds <typeparamref name="TRule"/> (a singleton, once however often it is added) to the rules the classifier ORs
    /// with the built-in one, and registers the classifier (<see cref="TryAddTransientFailureClassifier"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTransientFailureRule&lt;SwgohApiOutageRule&gt;();
    /// </code>
    /// </example>
    /// <typeparam name="TRule">The rule.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddTransientFailureRule<TRule>(
        this IServiceCollection services)
        where TRule : class, ITransientFailureRule
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITransientFailureRule, TRule>());
        return services.TryAddTransientFailureClassifier();
    }

    /// <summary>
    /// Adds <paramref name="isTransient"/> to the rules the classifier ORs with the built-in one, and registers the
    /// classifier (<see cref="TryAddTransientFailureClassifier"/>). It is asked about the exception and each of its
    /// causes in turn.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTransientFailureRule(exception => exception is SocketException);
    /// </code>
    /// </example>
    /// <param name="services">The service collection.</param>
    /// <param name="isTransient">Whether an exception (or one of its causes) is transient.</param>
    public static IServiceCollection AddTransientFailureRule(
        this IServiceCollection services,
        Func<Exception, bool> isTransient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(isTransient);
        services.AddSingleton<ITransientFailureRule>(new DelegateTransientFailureRule(isTransient));
        return services.TryAddTransientFailureClassifier();
    }
}
