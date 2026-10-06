using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Web.UnitTests.Support;

/// <summary>
/// The upstream of an HTTP client under test: answers each call as the script says (by call number, from 1), and
/// records when each came on the test's clock.
/// </summary>
internal sealed class ScriptedHandler(
    TimeProvider time,
    Func<int, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private readonly ConcurrentQueue<DateTimeOffset> _calls = new();

    public int Calls => _calls.Count;

    public DateTimeOffset[] CalledAt => [.. _calls];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _calls.Enqueue(time.GetUtcNow());
        return respond(_calls.Count, cancellationToken);
    }

    /// <summary>A gap between two calls.</summary>
    public TimeSpan Gap(int from, int to)
    {
        var calledAt = CalledAt;
        return calledAt[to - 1] - calledAt[from - 1];
    }
}
