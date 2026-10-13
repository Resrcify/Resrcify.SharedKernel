using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Registers SharedKernel's save interceptors in the container, so they are built with what it holds (the
/// <see cref="TimeProvider"/>, the outbox serializer and insert strategy), and adds them to a context.
/// </summary>
/// <remarks>
/// An interceptor made with <c>new</c> in <c>AddDbContext</c> reads the system clock unless it is handed one; one the
/// container builds gets the registered <see cref="TimeProvider"/> (a <c>FakeTimeProvider</c> in tests). The
/// interceptors keep no state of their own, so each is a singleton shared by every context. Their constructors are
/// unchanged: <c>new</c> still works.
/// </remarks>
/// <example>
/// <code>
/// services.AddEntityInterceptors().AddOutboxInterceptor();
/// services.AddDbContext&lt;AppDbContext&gt;((provider, options) => options
///     .UseNpgsql(connectionString)
///     .AddSaveChangesInterceptors(provider));
/// </code>
/// </example>
public static class SaveChangesInterceptorExtensions
{
    /// <summary>
    /// Registers <see cref="UpdateAuditableEntitiesInterceptor"/> and <see cref="UpdateDeletableEntitiesInterceptor"/>.
    /// </summary>
    public static IServiceCollection AddEntityInterceptors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<UpdateAuditableEntitiesInterceptor>();
        services.TryAddSingleton<UpdateDeletableEntitiesInterceptor>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="InsertOutboxMessagesInterceptor"/>, built from the registered <see cref="IOutboxSerializer"/>
    /// (<paramref name="serializer"/>, or <see cref="SystemTextJsonOutboxSerializer"/> when none is registered), the
    /// registered <see cref="IOutboxInsertStrategy"/> (the default one when none is), the <see cref="TimeProvider"/> and
    /// every <see cref="IOutboxSaveObserver"/>. The outbox job reads with the same registered serializer.
    /// </summary>
    public static IServiceCollection AddOutboxInterceptor(
        this IServiceCollection services,
        IOutboxSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (serializer is null)
            services.TryAddSingleton<IOutboxSerializer, SystemTextJsonOutboxSerializer>();
        else
            services.TryAddSingleton(serializer);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InsertOutboxMessagesInterceptor>();
        return services;
    }

    /// <summary>
    /// Adds the interceptors registered with <see cref="AddEntityInterceptors"/> and <see cref="AddOutboxInterceptor"/>
    /// to a context: the auditable, the deletable and (unless <paramref name="withOutbox"/> is <see langword="false"/>)
    /// the outbox one, those that are registered. With several contexts, pass <c>withOutbox: false</c> for one that
    /// doesn't map the outbox table.
    /// </summary>
    public static DbContextOptionsBuilder AddSaveChangesInterceptors(
        this DbContextOptionsBuilder options,
        IServiceProvider provider,
        bool withOutbox = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        var interceptors = new List<IInterceptor>(3);
        AddIfRegistered<UpdateAuditableEntitiesInterceptor>(interceptors, provider);
        AddIfRegistered<UpdateDeletableEntitiesInterceptor>(interceptors, provider);
        if (withOutbox)
            AddIfRegistered<InsertOutboxMessagesInterceptor>(interceptors, provider);
        return options.AddInterceptors(interceptors);
    }

    private static void AddIfRegistered<TInterceptor>(
        List<IInterceptor> interceptors,
        IServiceProvider provider)
        where TInterceptor : class, IInterceptor
    {
        if (provider.GetService<TInterceptor>() is { } interceptor)
            interceptors.Add(interceptor);
    }
}
