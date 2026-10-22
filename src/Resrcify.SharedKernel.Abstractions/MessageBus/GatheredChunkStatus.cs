namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>What came of one chunk of a chunked gather (<see cref="GatheredChunk{TResponse}"/>).</summary>
public enum GatheredChunkStatus
{
    /// <summary>Asked and gathered: <see cref="GatheredChunk{TResponse}.Gathered"/> holds what came back.</summary>
    Gathered,

    /// <summary>Asked, and its gather threw: <see cref="GatheredChunk{TResponse}.Exception"/> holds what it threw.</summary>
    Failed,

    /// <summary>
    /// Not asked: the budget had no room left for it. Reported once, for this chunk and every one after it
    /// (<see cref="GatheredChunk{TResponse}.Keys"/> holds all their keys), and the gather ends.
    /// </summary>
    NotAsked,
}
