using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;

/// <summary>
/// After a save of <typeparamref name="TContext"/> wrote outbox messages, sends <c>NOTIFY resrcify_outbox</c> with the
/// context's name, on the save's connection: inside the caller's transaction when there is one (PostgreSQL delivers it
/// when that commits, and never if it rolls back), otherwise right after the save committed.
/// </summary>
/// <remarks>
/// It runs a plain command on the context's connection, outside EF Core's execution strategy (a retrying strategy would
/// refuse it inside a transaction it didn't start). Outside a transaction the save has committed: whatever then goes
/// wrong (the connection can't be reopened, the pool is exhausted, the caller's token is cancelled) is logged and
/// dropped, never thrown at a caller whose work is saved; the outbox's next poll processes the messages. Inside a
/// transaction a failure is thrown, since the notification is part of what that transaction commits.
/// </remarks>
internal sealed partial class PostgresOutboxNotifier<TContext>(ILogger<PostgresOutboxNotifier<TContext>> logger)
    : IOutboxSaveObserver
    where TContext : DbContext
{
    internal static string Payload => typeof(TContext).Name;

    public async Task MessagesSavedAsync(
        DbContext context,
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken)
    {
        if (context is not TContext)
            return;

        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction is not null)
        {
            await NotifyAsync(context.Database, transaction, cancellationToken);
            return;
        }

        try
        {
            await NotifyAsync(context.Database, transaction: null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogNotifyFailed(exception, Payload);
        }
    }

    private static async Task NotifyAsync(
        DatabaseFacade database,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var connection = database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await NotifyAsync(connection, transaction, cancellationToken);
        }
        finally
        {
            if (opened)
                await database.CloseConnectionAsync();
        }
    }

    private static async Task NotifyAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_notify(@channel, @payload)";
        AddParameter(command, "channel", OutboxWakeUpOptions.Channel);
        AddParameter(command, "payload", Payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not notify the {DbContext} outbox of new messages; its next poll processes them")]
    private partial void LogNotifyFailed(Exception exception, string dbContext);
}
