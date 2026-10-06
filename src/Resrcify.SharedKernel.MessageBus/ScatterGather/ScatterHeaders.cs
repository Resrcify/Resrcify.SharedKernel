using System;
using System.Collections.Generic;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// Headers that tie a scatter request and its reply to a batch item. Requests and replies stay
/// plain DTOs; the responder copies these headers onto its reply.
/// </summary>
public static class ScatterHeaders
{
    public const string BatchId = "resrcify-scatter-batch-id";
    public const string ItemKey = "resrcify-scatter-item-key";
    public const string Kind = "resrcify-scatter-kind";

    public static bool TryRead(
        IReadOnlyDictionary<string, string> headers,
        out Guid batchId,
        out string itemKey)
    {
        ArgumentNullException.ThrowIfNull(headers);
        itemKey = string.Empty;
        batchId = Guid.Empty;
        return headers.TryGetValue(BatchId, out var rawBatchId) &&
            Guid.TryParse(rawBatchId, out batchId) &&
            headers.TryGetValue(ItemKey, out itemKey!);
    }

    internal static Dictionary<string, string> CopyFrom(IReadOnlyDictionary<string, string> headers)
    {
        var copied = new Dictionary<string, string>(StringComparer.Ordinal);
        if (headers.TryGetValue(BatchId, out var batchId))
            copied[BatchId] = batchId;
        if (headers.TryGetValue(ItemKey, out var itemKey))
            copied[ItemKey] = itemKey;
        if (headers.TryGetValue(Kind, out var kind))
            copied[Kind] = kind;
        return copied;
    }
}
