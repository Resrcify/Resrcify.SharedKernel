using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Exceptions;

/// <summary>
/// A request the client gave up on never reaches <c>ResultExceptionHandler</c>: <c>UseExceptionHandler()</c> answers it
/// itself (a 499, logged at Debug) before it asks any <see cref="IExceptionHandler"/>, so the handler has no branch for it.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultExceptionHandlerClientAbortTests
{
    [Fact]
    public async Task UseExceptionHandler_ShouldAnswerAClientAbortItself_WithoutAskingTheHandlers()
    {
        var asked = new AskedHandlers();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await TestHosts.StartAsync(
            services =>
            {
                services.AddSingleton(asked);
                // Asked first, before ResultExceptionHandler: it would see every exception the middleware hands over.
                services.AddExceptionHandler<RecordingHandler>();
                services.AddResultProblemDetails();
            },
            application =>
            {
                application.Use(async (context, next) =>
                {
                    await next(context);
                    if (context.Request.Path == "/slow")
                        answered.TrySetResult(context.Response.StatusCode);
                });
                application.UseExceptionHandler();
                application.MapGet("/boom", string () => throw new InvalidOperationException("boom"));
                application.MapGet("/slow", async (HttpContext context) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                });
            });
        using var client = app.GetTestClient();
        using var giveUp = new CancellationTokenSource();

        // The control: an exception that isn't a client abort is handed to the handlers, the recording one first.
        using var failed = await client.GetAsync(new Uri("/boom", UriKind.Relative));
        failed.StatusCode.ShouldBe(System.Net.HttpStatusCode.InternalServerError);
        asked.Count.ShouldBe(1);

        var sending = client.GetAsync(new Uri("/slow", UriKind.Relative), giveUp.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await giveUp.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => sending);
        (await answered.Task.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(StatusCodes.Status499ClientClosedRequest);
        asked.Count.ShouldBe(1);   // the control's only
    }

    private sealed class AskedHandlers
    {
        private int _count;

        public int Count
            => Volatile.Read(ref _count);

        public void Asked()
            => Interlocked.Increment(ref _count);
    }

    private sealed class RecordingHandler(AskedHandlers asked)
        : IExceptionHandler
    {
        public ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            asked.Asked();
            return ValueTask.FromResult(false);
        }
    }
}
