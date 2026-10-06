using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.UnitOfWork.Migrations;

/// <summary>Where <see cref="MigrateOnStartupService{TContext}"/> reads whether to migrate.</summary>
/// <param name="Section">The <c>Migrations</c> section; its <c>Run</c> key is read when the host starts.</param>
internal sealed record MigrateOnStartupSettings<TContext>(IConfigurationSection Section)
    where TContext : DbContext
{
    /// <summary>The DbContext these settings belong to (the type parameter keys them in DI).</summary>
    public static Type DbContextType => typeof(TContext);
}

/// <summary>
/// Applies <typeparamref name="TContext"/>'s pending migrations while the host starts, before any hosted service starts
/// (the web server, Quartz, the outbox lanes), when <c>Migrations:Run</c> is <see langword="true"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>It runs in <see cref="IHostedLifecycleService.StartingAsync"/>, which the host calls on every lifecycle service
/// before it starts any hosted service, so the app takes no traffic and runs no job on a schema it doesn't expect.</item>
/// <item>A failed migration fails the start (the pod restarts and tries again) rather than serve on the old schema.</item>
/// <item>Several instances starting at once are safe: EF Core 9+ takes a database lock around <c>Migrate</c> (on
/// PostgreSQL, Npgsql locks the history table <c>IN ACCESS EXCLUSIVE MODE</c>), and the instance that waited finds
/// the migrations applied.</item>
/// <item>EF Core creates the database and the history table before it takes that lock, so instances starting together
/// on a database that doesn't exist yet can collide creating them. The one that loses (see
/// <see cref="ConcurrentCreationRetry"/>) logs a warning and migrates again, up to
/// <see cref="ConcurrentCreationRetry.MaxAttempts"/> times, a little later each time; any other failure fails the start
/// at once.</item>
/// </list>
/// </remarks>
internal sealed partial class MigrateOnStartupService<TContext>(
    MigrateOnStartupSettings<TContext> settings,
    IServiceScopeFactory scopeFactory,
    ILogger<MigrateOnStartupService<TContext>> logger,
    TimeProvider? timeProvider = null)
    : IHostedLifecycleService
    where TContext : DbContext
{
    internal const string RunKey = "Run";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var context = typeof(TContext).Name;
        if (!ShouldRun())
        {
            LogSkipped(context, settings.Section.Path);
            return;
        }

        LogMigrating(context);
        var started = _time.GetTimestamp();
        try
        {
            await MigrateWithRetriesAsync(context, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception, context);
            throw;
        }

        var elapsed = _time.GetElapsedTime(started);
        LogMigrated(context, elapsed.TotalMilliseconds);
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Migrates, and again when another instance created the database or the history table at the same moment. Trying
    /// again is what a restart would do, sooner: <c>MigrateAsync</c> creates only what's missing and applies, under the
    /// migration lock, only the migrations nobody applied.
    /// </summary>
    private async Task MigrateWithRetriesAsync(
        string context,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt < ConcurrentCreationRetry.MaxAttempts; attempt++)
        {
            try
            {
                await MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && ConcurrentCreationRetry.IsConcurrentCreation(exception, out var sqlState))
            {
                var delay = ConcurrentCreationRetry.DelayAfter(attempt);
                LogLostCreationRace(
                    context,
                    sqlState,
                    exception.Message,
                    attempt,
                    ConcurrentCreationRetry.MaxAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, _time, cancellationToken);
            }
        }

        // The last try: whatever it throws fails the start.
        await MigrateAsync(cancellationToken);
    }

    /// <summary>One try, on a context of its own (a failed try leaves nothing behind for the next).</summary>
    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<TContext>()
            .Database
            .MigrateAsync(cancellationToken);
    }

    /// <summary><c>Migrations:Run</c>, read when the host starts; missing or empty is <see langword="false"/>.</summary>
    private bool ShouldRun()
    {
        var value = settings.Section[RunKey];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value, out var run))
            return run;

        throw new InvalidOperationException(
            $"{settings.Section.Path}:{RunKey} is '{value}'; it must be true or false.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Not migrating {DbContext}: {Section}:Run is not true")]
    private partial void LogSkipped(string dbContext, string section);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrating {DbContext} before the host starts")]
    private partial void LogMigrating(string dbContext);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated {DbContext} in {ElapsedMs} ms")]
    private partial void LogMigrated(string dbContext, double elapsedMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Migrating {DbContext} collided with another instance creating the same object (SQLSTATE {SqlState}: "
            + "{Reason}); try {Attempt} of {MaxAttempts} failed, trying again in {DelayMs} ms")]
    private partial void LogLostCreationRace(
        string dbContext,
        string sqlState,
        string reason,
        int attempt,
        int maxAttempts,
        double delayMs);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migrating {DbContext} failed; the host doesn't start")]
    private partial void LogFailed(Exception exception, string dbContext);
}
