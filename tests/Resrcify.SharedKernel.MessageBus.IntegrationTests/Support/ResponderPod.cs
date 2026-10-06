using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>
/// One instance of a responding service (think: one SwgohApi pod), wired the way a consumer would:
/// <c>AddMessageBus</c> with one <c>AddRateLimitedQueue</c>, gated on a health check the test toggles.
/// </summary>
internal sealed class ResponderPod : IAsyncDisposable
{
    public const string HealthTag = "upstream";

    private readonly IHost _host;
    private readonly PodState _state;

    private ResponderPod(IHost host, PodState state)
    {
        _host = host;
        _state = state;
    }

    public IReadOnlyList<DateTime> Handled => _state.Handled.ToList();

    /// <summary>Requests whose handling was cancelled (the queue stopped, or the request expired).</summary>
    public int Cancelled => _state.Cancelled;

    public static async Task<ResponderPod> StartAsync(
        RabbitMqConnection connection,
        string requestQueue,
        int perSecond,
        int burst = 1,
        int? prefetch = null,
        Func<PingRequest, Result<PingResponse>>? respond = null,
        IBusConfigurationStrategy? configuration = null,
        IRateLimiterStrategy? rateLimiter = null,
        TimeSpan? healthCheckInterval = null,
        TimeSpan? delay = null)
    {
        var state = new PodState(respond ?? (request => new PingResponse("pong " + request.Value)))
        {
            Delay = delay ?? TimeSpan.Zero,
        };
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(state);
        builder.Services
            .AddHealthChecks()
            .AddCheck(
                "upstream",
                () => state.IsHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(),
                tags: [HealthTag]);
        builder.Services.AddMessageBus(bus =>
        {
            bus.UseRabbitMq(connection)
                .AddMessage<PingRequest>(WireNames.PingRequest)
                .AddMessage<PingResponse>(WireNames.PingResponse)
                .AddRateLimitedQueue<PingRequest, PingResponse, PingHandler>(requestQueue, queue =>
                {
                    queue.PerSecond = perSecond;
                    queue.Burst = burst;
                    queue.Prefetch = prefetch;
                    queue.HealthCheckTag = HealthTag;
                    queue.HealthCheckInterval = healthCheckInterval ?? TimeSpan.FromMilliseconds(200);
                    if (rateLimiter is not null)
                        queue.RateLimiter = rateLimiter;
                });
            if (configuration is not null)
                bus.UseConfigurationStrategy(configuration);
        });

        var host = builder.Build();
        await host.StartAsync();
        return new ResponderPod(host, state);
    }

    /// <summary>Fails the pod's health check, as when its upstream (tunnel, game API) is down.</summary>
    public void MarkUnhealthy()
        => _state.IsHealthy = false;

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    internal sealed class PodState(Func<PingRequest, Result<PingResponse>> respond)
    {
        private int _cancelled;

        public ConcurrentQueue<DateTime> Handled { get; } = new();
        public Func<PingRequest, Result<PingResponse>> Respond { get; } = respond;
        public TimeSpan Delay { get; init; }
        public int Cancelled => Volatile.Read(ref _cancelled);
        public volatile bool IsHealthy = true;

        public void RecordCancelled()
            => Interlocked.Increment(ref _cancelled);
    }

    internal sealed class PingHandler(PodState state)
        : IRequestResponder<PingRequest, PingResponse>
    {
        public async Task<Result<PingResponse>> HandleAsync(PingRequest request, CancellationToken cancellationToken = default)
        {
            state.Handled.Enqueue(DateTime.UtcNow);
            try
            {
                await Task.Delay(state.Delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                state.RecordCancelled();
                throw;
            }
            return state.Respond(request);
        }
    }
}
