using System;
using System.Collections.Generic;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>Which <c>IScatterGatherClient</c> method a <see cref="ScatterGatherCall"/> came through.</summary>
public enum ScatterGatherCallKind
{
    /// <summary><c>RequestAsync</c>: one item, keyed <see cref="ScatterGatherClientStandIn.RequestKey"/>.</summary>
    Request,

    /// <summary><c>GatherAsync</c>, or one chunk of <c>GatherInChunksAsync</c>.</summary>
    Gather,

    /// <summary><c>StreamAsync</c>.</summary>
    Stream,
}

/// <summary>What a test double (<see cref="ScatterGatherClientStandIn"/>) was asked: one call, as it arrived.</summary>
/// <param name="Kind">The method it came through.</param>
/// <param name="RequestType">The request type (<c>TRequest</c>).</param>
/// <param name="Keys">The items, in the order given.</param>
/// <param name="Timeout">The timeout it was asked with.</param>
/// <param name="Requests">The requests, by key.</param>
public sealed record ScatterGatherCall(
    ScatterGatherCallKind Kind,
    Type RequestType,
    IReadOnlyList<string> Keys,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, object> Requests);
