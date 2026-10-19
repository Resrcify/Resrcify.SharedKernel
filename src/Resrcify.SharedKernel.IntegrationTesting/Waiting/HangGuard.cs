using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.IntegrationTesting.Waiting;

/// <summary>
/// Ends a wait that would never end. A test waits for its condition itself: a signal completed as the awaited call or
/// message arrives (<see cref="AwaitableLog{T}"/>, <see cref="CallCounter"/>, a <see cref="TaskCompletionSource"/>), or,
/// where only another process knows (a container's readiness, its database, its API), that state checked until it
/// holds (<see cref="UntilAsync"/>). None has a deadline a slow but correct run could miss: this guard only stops a test
/// whose condition never comes (a bug, a container that died) after <see cref="Limit"/>, far longer than any correct run
/// takes, saying what it waited for, instead of hanging the run.
/// </summary>
/// <remarks>
/// The limit is a hang detector, not a timing assertion: a passing run never reaches it, so a long one costs nothing but
/// the time a broken run takes to fail. Ten minutes covers the slowest correct waits the services have (a cold image
/// pull on a CI runner, a game-data download through a stand-in) with room to spare; a suite that wants a broken run to
/// fail sooner passes its own <c>limit</c>.
/// </remarks>
public static class HangGuard
{
    /// <summary>How long a wait may last before the test is taken to be hung (10 minutes).</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    /// <summary>How often <see cref="UntilAsync"/> checks state that has no signal of its own (250 ms).</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(250);

    /// <summary>Waits for <paramref name="task"/> (a signal), failing only if it hangs.</summary>
    /// <param name="task">The signal: completes when what the test waits for has happened.</param>
    /// <param name="waitingFor">What the test waits for, for the message if it hangs.</param>
    /// <param name="diagnostics">What the message adds if it hangs (e.g. a container's log).</param>
    /// <param name="limit">How long before the wait counts as hung; <see cref="Limit"/> when not given.</param>
    /// <exception cref="TimeoutException">The signal didn't come within the limit.</exception>
    public static async Task<T> AwaitAsync<T>(
        Task<T> task,
        string waitingFor,
        Func<Task<string>>? diagnostics = null,
        TimeSpan? limit = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        var hungAfter = limit ?? Limit;
        try
        {
            return await task.WaitAsync(hungAfter);
        }
        catch (TimeoutException exception)
        {
            throw await HungAsync(waitingFor, hungAfter, diagnostics, exception);
        }
    }

    /// <inheritdoc cref="AwaitAsync{T}"/>
    public static async Task AwaitAsync(
        Task task,
        string waitingFor,
        Func<Task<string>>? diagnostics = null,
        TimeSpan? limit = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        var hungAfter = limit ?? Limit;
        try
        {
            await task.WaitAsync(hungAfter);
        }
        catch (TimeoutException exception)
        {
            throw await HungAsync(waitingFor, hungAfter, diagnostics, exception);
        }
    }

    /// <summary>
    /// Checks <paramref name="holds"/> every <paramref name="checkEvery"/> until it is true: only for state another
    /// process knows, with no signal to wait on. The interval only spaces the checks; the wait ends with the condition.
    /// </summary>
    /// <param name="holds">Whether the condition holds now.</param>
    /// <param name="waitingFor">What the test waits for, for the message if it hangs.</param>
    /// <param name="diagnostics">What the message adds if it hangs (e.g. the last state seen).</param>
    /// <param name="limit">How long before the wait counts as hung; <see cref="Limit"/> when not given.</param>
    /// <param name="checkEvery">The pause between two checks; <see cref="CheckEvery"/> when not given.</param>
    /// <exception cref="TimeoutException">The condition didn't hold within the limit.</exception>
    public static async Task UntilAsync(
        Func<Task<bool>> holds,
        string waitingFor,
        Func<Task<string>>? diagnostics = null,
        TimeSpan? limit = null,
        TimeSpan? checkEvery = null)
    {
        ArgumentNullException.ThrowIfNull(holds);
        var hungAfter = limit ?? Limit;
        var pause = checkEvery ?? CheckEvery;
        var started = TimeProvider.System.GetTimestamp();
        while (!await holds())
        {
            if (TimeProvider.System.GetElapsedTime(started) >= hungAfter)
                throw await HungAsync(waitingFor, hungAfter, diagnostics, inner: null);

            await Task.Delay(pause);
        }
    }

    private static async Task<TimeoutException> HungAsync(
        string waitingFor,
        TimeSpan limit,
        Func<Task<string>>? diagnostics,
        Exception? inner)
    {
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"Hung: waited {limit} for {waitingFor}, far longer than a correct run takes.");
        if (diagnostics is not null)
            message += Environment.NewLine + await diagnostics();

        return new TimeoutException(message, inner);
    }
}
