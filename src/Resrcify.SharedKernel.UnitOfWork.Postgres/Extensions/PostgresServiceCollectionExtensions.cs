using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;

/// <summary>Sets a DbContext up on PostgreSQL the way every service does, in one call.</summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> on PostgreSQL:
    /// <list type="bullet">
    /// <item>the connection from the <c>Database</c> section (<see cref="PostgresOptions"/>: <c>Host</c>, <c>Port</c>,
    /// <c>Database</c>, <c>Username</c>, <c>Password</c>, optionally <c>CommandTimeoutInSeconds</c>), validated when the
    /// host starts, the connection string built by <c>NpgsqlConnectionStringBuilder</c>;</item>
    /// <item>the auditable and soft-delete interceptors, built by the container (with its <see cref="TimeProvider"/>);</item>
    /// <item><c>IUnitOfWork</c> as <c>UnitOfWork&lt;TContext&gt;</c>, unless one is registered;</item>
    /// <item>what <paramref name="configure"/> opts into: <c>WithOutbox()</c>, <c>WithOutboxWakeUp()</c>,
    /// <c>RetryOnFailure()</c>, and the service's own Npgsql / EF Core options.</item>
    /// </list>
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddPostgresDbContext&lt;ShardDbContext&gt;(configuration, db => db
    ///     .WithOutbox(outbox => outbox.OnConflictDoNothing = true)
    ///     .WithOutboxWakeUp()
    ///     .RetryOnFailure());
    /// </code>
    /// </example>
    public static IServiceCollection AddPostgresDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PostgresDbContextBuilder>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = new PostgresDbContextBuilder();
        configure?.Invoke(builder);

        services.AddConnection<TContext>(configuration, builder);
        services.AddEntityInterceptors();
        if (builder.Outbox is { } outbox)
            services.AddOutboxWriting<TContext>(outbox);
        if (builder.WakeUp is { } wakeUp)
            services.AddOutboxWakeUp<TContext>(builder, wakeUp);

        services.TryAddScoped<IUnitOfWork, UnitOfWork<TContext>>();
        services.AddDbContext<TContext>((provider, options) =>
        {
            builder.Apply(options, provider.GetRequiredService<PostgresConnection<TContext>>().ConnectionString);
            options.AddSaveChangesInterceptors(provider, withOutbox: false);
            // This context's own outbox interceptor: its insert strategy is this context's (OnConflictDoNothing on one
            // context mustn't write another's outbox with ON CONFLICT, which needs that context's unique index).
            if (builder.Outbox is not null)
                options.AddInterceptors(provider.GetRequiredService<PostgresOutboxInterceptor<TContext>>().Interceptor);
        });
        return services;
    }

    /// <summary>
    /// The connection string <c>AddPostgresDbContext&lt;TContext&gt;</c> built, e.g. for a health check:
    /// <c>AddNpgSql(provider =&gt; provider.GetPostgresConnectionString&lt;ShardDbContext&gt;())</c>.
    /// </summary>
    public static string GetPostgresConnectionString<TContext>(this IServiceProvider provider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetService<PostgresConnection<TContext>>()?.ConnectionString
            ?? throw new InvalidOperationException(
                $"{typeof(TContext).Name} has no PostgreSQL connection: register it with AddPostgresDbContext<{typeof(TContext).Name}>().");
    }

    private static void AddConnection<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        PostgresDbContextBuilder builder)
        where TContext : DbContext
    {
        var section = configuration.GetSection(builder.SectionName);
        var name = PostgresDbContextSettings<TContext>.OptionsName;
        services
            .AddOptions<PostgresOptions>(name)
            .Bind(section)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PostgresOptions>>(new PostgresOptionsValidator(name, section.Path));
        services.RemoveAll<PostgresDbContextSettings<TContext>>();
        services.AddSingleton(new PostgresDbContextSettings<TContext>(builder));
        services.TryAddSingleton<PostgresConnection<TContext>>();
    }

    private static void AddOutboxWriting<TContext>(
        this IServiceCollection services,
        PostgresOutboxOptions outbox)
        where TContext : DbContext
    {
        EnsureOneSerializer<TContext>(services, outbox.Serializer);
        services.AddOutboxInterceptor(outbox.Serializer);
        services.RemoveAll<PostgresOutboxInterceptor<TContext>>();
        services.AddSingleton(provider => new PostgresOutboxInterceptor<TContext>(new InsertOutboxMessagesInterceptor(
            provider.GetRequiredService<IOutboxSerializer>(),
            outbox.OnConflictDoNothing
                ? new PostgresOnConflictOutboxInsertStrategy()
                : provider.GetService<IOutboxInsertStrategy>(),
            provider.GetService<TimeProvider>(),
            provider.GetServices<IOutboxSaveObserver>(),
            provider.GetService<ILogger<InsertOutboxMessagesInterceptor>>())));
    }

    // Every outbox job reads with the one registered IOutboxSerializer, so every context must write with it: a second,
    // different one would write messages the jobs can't read.
    private static void EnsureOneSerializer<TContext>(
        IServiceCollection services,
        IOutboxSerializer? serializer)
        where TContext : DbContext
    {
        if (serializer is null)
            return;

        var registered = services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(IOutboxSerializer));
        var registeredType = registered?.ImplementationInstance?.GetType() ?? registered?.ImplementationType;
        if (registered is not null && !ReferenceEquals(registered.ImplementationInstance, serializer) && registeredType != serializer.GetType())
            throw new InvalidOperationException(
                $"{typeof(TContext).Name}'s outbox asks for {serializer.GetType().Name}, but {registeredType?.Name ?? "another serializer"} "
                + "is registered already (by another context's outbox, or AddOutboxProcessing): the outbox jobs read every "
                + "message with that one. Give every context the same serializer.");
    }

    private static void AddOutboxWakeUp<TContext>(
        this IServiceCollection services,
        PostgresDbContextBuilder builder,
        OutboxWakeUpOptions wakeUp)
        where TContext : DbContext
    {
        if (builder.Outbox is null)
            throw new InvalidOperationException(
                $"WithOutboxWakeUp() needs WithOutbox() on {typeof(TContext).Name}: only the outbox interceptor knows when a save wrote messages.");

        services.RemoveAll<OutboxWakeUpSettings<TContext>>();
        services.AddSingleton(new OutboxWakeUpSettings<TContext>(wakeUp));
        services.TryAddSingleton<OutboxWakeUp<TContext>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxSaveObserver, PostgresOutboxNotifier<TContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PostgresOutboxListener<TContext>>());
    }
}
