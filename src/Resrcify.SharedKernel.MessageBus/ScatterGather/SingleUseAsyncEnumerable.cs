using System;
using System.Collections.Generic;
using System.Threading;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// An async sequence that can be enumerated once: a streamed batch is sent when enumerated, so a second enumeration
/// (a <c>ToListAsync</c> after a loop) would send it again and spend a rate-limited upstream's budget twice.
/// </summary>
internal sealed class SingleUseAsyncEnumerable<T>(IAsyncEnumerable<T> inner)
    : IAsyncEnumerable<T>
{
    private int _enumerated;

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => Interlocked.Exchange(ref _enumerated, 1) == 0
            ? inner.GetAsyncEnumerator(cancellationToken)
            : throw new InvalidOperationException(
                "A scatter-gather stream can be read once: reading it again would send its requests again. Keep the "
                + "replies as you read them, or stream again.");
}
