using System;
using System.Globalization;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// A duration in a job's data: stored in milliseconds under its key, or (a job stored before 4.3.0 in a persistent
/// Quartz store) in whole seconds or days under the key the int options had.
/// </summary>
internal static class JobDataDurations
{
    /// <summary>
    /// The duration under <paramref name="key"/> (milliseconds: an int, a long or a string, or a <see cref="TimeSpan"/>),
    /// else the one under <paramref name="legacyKey"/> in <paramref name="legacyUnit"/>s, else
    /// <paramref name="fallback"/>. A negative or unusable value falls back.
    /// </summary>
    public static TimeSpan Read(
        JobDataMap data,
        string key,
        string legacyKey,
        TimeSpan legacyUnit,
        TimeSpan fallback)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.TryGetValue(key, out var value) && Number(value) is { } milliseconds and >= 0)
            return TimeSpan.FromMilliseconds(milliseconds);
        if (data.TryGetValue(legacyKey, out var legacy) && Number(legacy) is { } units and >= 0)
            return legacyUnit * units;
        return fallback;
    }

    private static long? Number(object? value)
        => value switch
        {
            int number => number,
            long number => number,
            TimeSpan duration => (long)duration.TotalMilliseconds,
            string text when long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
}
