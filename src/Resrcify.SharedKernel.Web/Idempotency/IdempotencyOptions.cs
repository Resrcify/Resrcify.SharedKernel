using System;

namespace Resrcify.SharedKernel.Web.Idempotency;

/// <summary>How <c>UseIdempotency</c> treats requests to endpoints marked <c>WithIdempotency()</c>.</summary>
public sealed class IdempotencyOptions
{
    /// <summary>The request header that carries the key.</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>How long a response is kept, and replayed to a request repeating its key.</summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The longest a request holds its key while it is handled: a repeat meanwhile is answered 409. A request that runs
    /// longer (or an instance that died) lets a repeat through after this.
    /// </summary>
    public TimeSpan InProgressTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The longest key accepted.</summary>
    public int MaxKeyLength { get; set; } = 255;
}
