using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.UnitOfWork.Migrations;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>Applies a DbContext's migrations when the host starts.</summary>
public static class MigrationsServiceCollectionExtensions
{
    /// <summary>The configuration section the services keep their migration switch in (<c>Migrations:Run</c>).</summary>
    public const string SectionName = "Migrations";

    /// <summary>
    /// Applies <typeparamref name="TContext"/>'s pending migrations (<c>MigrateAsync</c>) while the host starts, before
    /// the web server and every other hosted service start, when <c>Migrations:Run</c> is <see langword="true"/>. The
    /// switch is read at start-up, so a value a test host adds later still counts. Nothing is built or run during
    /// registration.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration holding the <c>Migrations</c> section.</param>
    /// <param name="sectionName">Another section holding the <c>Run</c> key.</param>
    /// <example>
    /// Replaces <c>if (migrations.Run) services.ApplyMigrations&lt;ShardDbContext&gt;();</c>:
    /// <code>services.AddMigrationsOnStartup&lt;ShardDbContext&gt;(configuration);</code>
    /// </example>
    public static IServiceCollection AddMigrationsOnStartup<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = SectionName)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.TryAddSingleton(TimeProvider.System);
        services.RemoveAll<MigrateOnStartupSettings<TContext>>();
        services.AddSingleton(new MigrateOnStartupSettings<TContext>(configuration.GetSection(sectionName)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MigrateOnStartupService<TContext>>());
        return services;
    }
}
