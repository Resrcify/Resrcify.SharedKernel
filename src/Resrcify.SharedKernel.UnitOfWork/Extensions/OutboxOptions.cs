namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Tuning knobs for the outbox processing job, surfaced through
/// <see cref="OutboxServiceCollectionExtensions.AddOutboxProcessing{TDbContext}"/>.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>Maximum number of messages claimed per processing cycle.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Seconds between processing cycles.</summary>
    public int ProcessIntervalInSeconds { get; set; } = 60;

    /// <summary>Seconds to wait after startup before the first cycle.</summary>
    public int DelayInSecondsBeforeStart { get; set; } = 60;

    /// <summary>
    /// Attempts a message gets before it is treated as poison and skipped by the
    /// polling query.
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
}
