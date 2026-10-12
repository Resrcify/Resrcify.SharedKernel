using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ErrorQueues;

/// <summary>
/// The service's error queue (<c>&lt;input queue&gt;.error</c>), on whichever transport the service runs: RabbitMQ, or
/// the in-memory network (<c>UseInMemory</c>, the test harness). Every change is logged.
/// </summary>
internal sealed partial class ErrorQueue(
    MessageBusSettings settings,
    IServiceProvider provider,
    ILogger<ErrorQueue> logger)
    : IErrorQueue
{
    // Read when used: the test harness moves the service to the in-memory network after it is registered.
    private IErrorQueueStore Store
        => settings.InMemoryNetwork is { } network
            ? new InMemoryErrorQueueStore(network)
            : new RabbitMqErrorQueueStore(settings, provider);

    public async Task<Result<ErrorQueueContents>> ListAsync(
        int max = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(max);
        if (settings.ErrorQueue is not { } queue)
            return ErrorQueueErrors.None;

        return await TryAsync(async store =>
        {
            var count = await store.CountAsync(queue, cancellationToken);
            var messages = new List<FailedMessage>();
            await store.ScanAsync(
                queue,
                max,
                message =>
                {
                    messages.Add(StoredMessageHeaders.ToFailedMessage(message));
                    return ScanStep.Keep;
                },
                cancellationToken);
            return Result.Success(new ErrorQueueContents(queue, count, messages));
        });
    }

    public async Task<Result> DeleteAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var deleted = await DeleteWhereAsync(
            message => StoredMessageHeaders.IdOf(message) == messageId,
            messageId,
            cancellationToken);
        if (deleted.IsFailure)
            return Result.Failure(deleted.Errors);
        return deleted.Value == 0
            ? Result.Failure(ErrorQueueErrors.MessageNotFound(messageId))
            : Result.Success();
    }

    public Task<Result<int>> DeleteAllAsync(
        string? messageType = null,
        CancellationToken cancellationToken = default)
        => DeleteWhereAsync(
            message => messageType is null || StoredMessageHeaders.TypeOf(message) == messageType,
            messageType is null ? "every message" : $"every {messageType}",
            cancellationToken);

    public async Task<Result> RetryAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var moved = await MoveBackWhereAsync(
            message => StoredMessageHeaders.IdOf(message) == messageId,
            messageId,
            cancellationToken);
        if (moved.IsFailure)
            return Result.Failure(moved.Errors);

        var (results, notMovable) = moved.Value;
        if (results.Count == 0 && notMovable == 0)
            return Result.Failure(ErrorQueueErrors.MessageNotFound(messageId));
        if (notMovable > 0)
            return Result.Failure(ErrorQueueErrors.NotMoved(messageId, "it doesn't say which queue it failed on"));
        return results.FirstOrDefault(result => !result.Succeeded) is { } failed
            ? Result.Failure(ErrorQueueErrors.NotMoved(messageId, failed.Failure!))
            : Result.Success();
    }

    public async Task<Result<int>> RetryAllAsync(
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        var moved = await MoveBackWhereAsync(
            message => messageType is null || StoredMessageHeaders.TypeOf(message) == messageType,
            messageType is null ? "every message" : $"every {messageType}",
            cancellationToken);
        return moved.IsFailure
            ? Result.Failure<int>(moved.Errors)
            : moved.Value.Results.Count(result => result.Succeeded);
    }

    private async Task<Result<int>> DeleteWhereAsync(
        Func<StoredMessage, bool> matches,
        string what,
        CancellationToken cancellationToken)
    {
        if (settings.ErrorQueue is not { } queue)
            return ErrorQueueErrors.None;

        return await TryAsync(async store =>
        {
            var results = await store.ScanAsync(
                queue,
                int.MaxValue,
                message => matches(message) ? ScanStep.Delete : ScanStep.Keep,
                cancellationToken);
            if (results.Count > 0)
                LogDeleted(queue, results.Count, what);
            return Result.Success(results.Count);
        });
    }

    /// <summary>
    /// Moves the matching messages back to the queue each failed on. Answers what was done with each, and how many
    /// matched but don't say where they failed (kept).
    /// </summary>
    private async Task<Result<(IReadOnlyList<ScanResult> Results, int NotMovable)>> MoveBackWhereAsync(
        Func<StoredMessage, bool> matches,
        string what,
        CancellationToken cancellationToken)
    {
        if (settings.ErrorQueue is not { } queue)
            return ErrorQueueErrors.None;

        return await TryAsync(async store =>
        {
            var notMovable = 0;
            var results = await store.ScanAsync(
                queue,
                int.MaxValue,
                message =>
                {
                    if (!matches(message))
                        return ScanStep.Keep;
                    if (StoredMessageHeaders.SourceQueueOf(message) is { } source)
                        return ScanStep.MoveTo(source);
                    notMovable++;
                    LogNoSourceQueue(queue, StoredMessageHeaders.IdOf(message));
                    return ScanStep.Keep;
                },
                cancellationToken);

            var moved = results.Count(result => result.Succeeded);
            if (moved > 0)
                LogMovedBack(queue, moved, what);
            foreach (var failed in results.Where(result => !result.Succeeded))
                LogNotMoved(queue, StoredMessageHeaders.IdOf(failed.Message), failed.Failure!);
            return Result.Success<(IReadOnlyList<ScanResult>, int)>((results, notMovable));
        });
    }

    private async Task<Result<T>> TryAsync<T>(Func<IErrorQueueStore, Task<Result<T>>> operation)
    {
        try
        {
            return await operation(Store);
        }
        catch (Exception exception) when (exception is BrokerUnreachableException or OperationInterruptedException or AlreadyClosedException or TimeoutException)
        {
            LogUnavailable(exception, settings.ErrorQueue);
            return ErrorQueueErrors.Unavailable(exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Error queue {Queue}: deleted {Count} message(s) ({What})")]
    private partial void LogDeleted(string queue, int count, string what);

    [LoggerMessage(Level = LogLevel.Information, Message = "Error queue {Queue}: moved {Count} message(s) back ({What})")]
    private partial void LogMovedBack(string queue, int count, string what);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error queue {Queue}: message {MessageId} wasn't moved back ({Reason}); it stays")]
    private partial void LogNotMoved(string queue, string? messageId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error queue {Queue}: message {MessageId} doesn't say which queue it failed on, so it stays")]
    private partial void LogNoSourceQueue(string queue, string? messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error queue {Queue} can't be reached")]
    private partial void LogUnavailable(Exception exception, string? queue);
}
