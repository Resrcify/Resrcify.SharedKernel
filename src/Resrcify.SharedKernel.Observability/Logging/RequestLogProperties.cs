namespace Resrcify.SharedKernel.Observability.Logging;

/// <summary>
/// The properties <c>UseServiceRequestLogging()</c> adds to each request's log line, besides Serilog's own
/// (<c>RequestMethod</c>, <c>RequestPath</c>, <c>StatusCode</c>, <c>Elapsed</c>) and the <c>TraceId</c> and
/// <c>SpanId</c> every event carries.
/// </summary>
public static class RequestLogProperties
{
    /// <summary>The request's <c>Host</c> header (host and port), when it has one.</summary>
    public const string RequestHost = "RequestHost";

    /// <summary>
    /// The caller's IP address (<c>HttpContext.Connection.RemoteIpAddress</c>; an IPv4 address mapped to IPv6 is
    /// written as IPv4), when known. Behind a proxy it is the proxy's, unless the service runs
    /// <c>UseForwardedHeaders()</c> before it.
    /// </summary>
    public const string ClientIp = "ClientIp";

    /// <summary>
    /// The caller's user id: the <c>ClaimTypes.NameIdentifier</c> claim (the one Web's <c>GetUserId()</c> reads), when
    /// the request is authenticated and carries it.
    /// </summary>
    public const string UserId = "UserId";
}
