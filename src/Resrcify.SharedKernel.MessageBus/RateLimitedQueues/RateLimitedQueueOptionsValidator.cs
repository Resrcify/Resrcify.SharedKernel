using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Abstractions.Resilience;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// Checks one rate-limited queue's <see cref="RateLimitedQueueOptions"/> (the named options of its name) when the host
/// starts, naming the queue and each key that is wrong: what the queue's host assumes, so a bad value fails the start
/// instead of a bus that never starts (a token bucket at 0/s) or a health gate that spins (an interval of 0).
/// </summary>
/// <param name="queueName">The queue, whose named options this checks; any other name is skipped.</param>
internal sealed class RateLimitedQueueOptionsValidator(string queueName)
    : IValidateOptions<RateLimitedQueueOptions>
{
    /// <summary>The longest wait <c>Task.Delay</c> takes (<see cref="uint.MaxValue"/> - 1 milliseconds, about 49.7 days).</summary>
    internal static readonly TimeSpan LongestWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1.0);

    private static readonly string LongestWaitInDays = LongestWait.TotalDays.ToString("0.#", CultureInfo.InvariantCulture);

    public ValidateOptionsResult Validate(
        string? name,
        RateLimitedQueueOptions options)
    {
        if (!string.Equals(name, queueName, StringComparison.Ordinal))
            return ValidateOptionsResult.Skip;
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.PerSecond < 1)
            failures.Add(Failure(nameof(RateLimitedQueueOptions.PerSecond), "must be at least 1", options.PerSecond));
        if (options.Burst < 0)
            failures.Add(Failure(nameof(RateLimitedQueueOptions.Burst), "can't be negative", options.Burst));
        if (options.Prefetch is < 1)
            failures.Add(Failure(nameof(RateLimitedQueueOptions.Prefetch), "must be at least 1 (or unset: one second of work)", options.Prefetch));
        if (options.HealthCheckTag is { } tag && string.IsNullOrWhiteSpace(tag))
            failures.Add(Failure(nameof(RateLimitedQueueOptions.HealthCheckTag), "must name a tag (or be unset: no gating)", $"'{tag}'"));
        if (!IsWait(options.HealthCheckInterval) || options.HealthCheckInterval == TimeSpan.Zero)
            failures.Add(Failure(nameof(RateLimitedQueueOptions.HealthCheckInterval), $"must be positive and at most {LongestWaitInDays} days", options.HealthCheckInterval));
        if (options.RateLimiter is null)
            failures.Add($"{Key(nameof(RateLimitedQueueOptions.RateLimiter))} is required (TokenBucketRateLimiterStrategy.Instance by default).");
        if (options.AnswerFailure is null)
            failures.Add($"{Key(nameof(RateLimitedQueueOptions.AnswerFailure))} is required (AnswerFailureWhenItsTheRequestsFault by default).");
        if (options.Retries is { } retries)
            AddRetryFailures(failures, retries);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    // A RetrySchedule refuses fewer than 1 attempt and negative delays as it is made, but not a wait longer than
    // Task.Delay takes, which would throw on the request's first retry instead.
    private void AddRetryFailures(
        List<string> failures,
        RetrySchedule retries)
    {
        const string retriesKey = nameof(RateLimitedQueueOptions.Retries);
        if (!IsWait(retries.FirstDelay))
            failures.Add(Failure($"{retriesKey}:{nameof(RetrySchedule.FirstDelay)}", $"must be between 0 and {LongestWaitInDays} days", retries.FirstDelay));
        if (!IsWait(retries.MaxDelay))
            failures.Add(Failure($"{retriesKey}:{nameof(RetrySchedule.MaxDelay)}", $"must be between 0 and {LongestWaitInDays} days", retries.MaxDelay));
    }

    private static bool IsWait(TimeSpan wait)
        => wait >= TimeSpan.Zero && wait <= LongestWait;

    private string Failure(
        string key,
        string rule,
        object? value)
        => string.Create(CultureInfo.InvariantCulture, $"{Key(key)} {rule}; it is {value}.");

    private string Key(string key)
        => $"The rate-limited queue {queueName}'s {key}";
}
