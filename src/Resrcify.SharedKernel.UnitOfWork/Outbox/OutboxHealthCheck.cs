using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// Whether <typeparamref name="TDbContext"/>'s outbox keeps up: it fails (with the registration's failure status) when
/// the oldest waiting message has waited longer than <paramref name="maxWaitingAge"/>, or when the backlog hasn't been
/// measured for three intervals (the database is unreachable). Messages that gave up don't fail it: they are counted
/// in its data and in <c>outbox.messages.poison</c>, for an alert rather than a restart.
/// </summary>
internal sealed class OutboxHealthCheck<TDbContext>(
    OutboxBacklogMonitor<TDbContext> monitor,
    TimeSpan maxWaitingAge,
    TimeProvider time)
    : IHealthCheck
    where TDbContext : DbContext
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = time.GetUtcNow();
        if (monitor.Latest is not { } backlog)
            return Task.FromResult(NeverMeasured(context, now));

        var sinceMeasured = now - backlog.MeasuredAt;
        // The oldest message has kept waiting since it was measured (unless it was processed meanwhile).
        var oldestAge = backlog.Waiting > 0 ? backlog.OldestWaitingAge + sinceMeasured : TimeSpan.Zero;
        var data = new Dictionary<string, object>
        {
            ["waiting"] = backlog.Waiting,
            ["poison"] = backlog.Poison,
            ["waiting_for_later_try"] = backlog.WaitingForLaterTry,
            ["oldest_waiting_age_seconds"] = Math.Round(oldestAge.TotalSeconds),
            ["measured_seconds_ago"] = Math.Round(sinceMeasured.TotalSeconds),
        };

        if (sinceMeasured > 3 * monitor.Interval)
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                $"The outbox backlog hasn't been measured for {sinceMeasured:g}: is the database reachable, and its outbox table migrated?",
                data: data));

        if (oldestAge > maxWaitingAge)
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{backlog.Waiting} outbox messages wait; the oldest for {oldestAge:g} (more than {maxWaitingAge:g}).",
                data: data));

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{backlog.Waiting} outbox messages wait, {backlog.Poison} gave up.",
            data));
    }

    // Not measured yet is fine for a moment after start-up, not for three intervals: the database is unreachable or the
    // outbox table missing (no migrations), and every measurement so far failed.
    private HealthCheckResult NeverMeasured(
        HealthCheckContext context,
        DateTimeOffset now)
    {
        if (monitor.StartedAt is not { } started || now - started <= 3 * monitor.Interval)
            return HealthCheckResult.Healthy("The outbox backlog hasn't been measured yet.");

        return new HealthCheckResult(
            context.Registration.FailureStatus,
            $"The outbox backlog hasn't been measured since the monitor started {now - started:g} ago: is the database "
            + "reachable, and the outbox table there and migrated (SharedKernel 4.0 adds OutboxMessages.NextAttemptOnUtc)?");
    }
}
