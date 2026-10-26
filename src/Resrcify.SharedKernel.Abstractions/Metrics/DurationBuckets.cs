using System.Collections.Generic;

namespace Resrcify.SharedKernel.Abstractions.Metrics;

/// <summary>
/// The bucket boundaries of a duration histogram measured in seconds (<c>unit: "s"</c>), handed to
/// <c>Meter.CreateHistogram</c> as its advice: <c>advice: new() { HistogramBucketBoundaries = DurationBuckets.Seconds }</c>.
/// Every SharedKernel duration histogram takes them, and a service's own should too.
/// </summary>
/// <remarks>
/// Why: a histogram without advice gets OpenTelemetry's default boundaries (0, 5, 10, 25, 50, 75, 100, 250, 500, 750,
/// 1000, 2500, 5000, 7500, 10000), which are sized for milliseconds. A duration in seconds then falls almost always into
/// the first bucket or two, and a quantile is a fixed fraction of a bucket's edge (a "p50 of 2.5 s" is the middle of the
/// 0-5 s bucket, whatever the durations were). These run from 5 ms (an in-memory request) to an hour (a long scatter-gather
/// batch, a message that waited in the outbox), with the most resolution between 0.1 s and 10 s, where a request that
/// calls another service lands.
/// </remarks>
public static class DurationBuckets
{
    /// <summary>The boundaries in seconds: 5 ms to 1 h.</summary>
    public static IReadOnlyList<double> Seconds { get; } =
    [
        0.005,
        0.01,
        0.025,
        0.05,
        0.1,
        0.25,
        0.5,
        1,
        2.5,
        5,
        7.5,
        10,
        15,
        30,
        60,
        120,
        300,
        600,
        1800,
        3600,
    ];
}
