using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.IntegrationTesting.Waiting;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Migrations;

/// <summary>How the instances held at <c>CREATE DATABASE</c> are let go.</summary>
internal enum CreationOrder
{
    /// <summary>All at once: their <c>CREATE DATABASE</c> statements reach the server together.</summary>
    Together,

    /// <summary>
    /// One first, the others once its <c>CREATE DATABASE</c> has committed: each of them then finds the database it
    /// is creating already there (<c>42P04 duplicate_database</c>), the race a slower pod loses.
    /// </summary>
    OneFirst,
}

/// <summary>
/// Holds every instance's <c>CREATE DATABASE</c> until all of them have reached it, so the instances create the
/// database at once for certain, not by luck of timing; then lets them go in <see cref="CreationOrder"/>. An instance
/// reaches it only after finding the database missing (EF Core checks first), so all of them do. A later
/// <c>CREATE DATABASE</c> (none is expected: a retry finds the database) goes through. Waits are signals, under
/// <see cref="HangGuard"/>.
/// </summary>
/// <param name="instances">How many instances must reach <c>CREATE DATABASE</c> before any goes on.</param>
/// <param name="order">How they are let go.</param>
internal sealed class DatabaseCreationGate(
    int instances,
    CreationOrder order)
    : DbCommandInterceptor
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DbCommand? _first;
    private int _arrived;

    /// <summary>How many <c>CREATE DATABASE</c> statements reached the gate.</summary>
    public int Arrived
        => Volatile.Read(ref _arrived);

    /// <summary>Whether all <paramref name="instances"/> were held together and let go.</summary>
    public bool HeldTogether
        => _allArrived.Task.IsCompletedSuccessfully;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCreateDatabase(command))
            return result;

        var arrival = Interlocked.Increment(ref _arrived);
        if (arrival > instances)
            return result;

        if (arrival == 1)
            Volatile.Write(ref _first, command);
        if (arrival == instances)
            _allArrived.TrySetResult();

        await HangGuard.AwaitAsync(_allArrived.Task, $"all {instances} instances to reach CREATE DATABASE");
        if (order == CreationOrder.OneFirst && arrival != 1)
            await HangGuard.AwaitAsync(_firstDone.Task, "the first instance's CREATE DATABASE to commit");
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ReleaseAfterTheFirst(command);
        return ValueTask.FromResult(result);
    }

    // Were the first to fail, the others go on all the same (and the test sees the failure), rather than wait for ever.
    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ReleaseAfterTheFirst(command);
        return Task.CompletedTask;
    }

    private void ReleaseAfterTheFirst(DbCommand command)
    {
        if (ReferenceEquals(command, Volatile.Read(ref _first)))
            _firstDone.TrySetResult();
    }

    private static bool IsCreateDatabase(DbCommand command)
        => command.CommandText.TrimStart().StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase);
}
