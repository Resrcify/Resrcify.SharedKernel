using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Idempotency;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class IdempotencyMiddlewareTests
{
    private readonly Orders _orders = new();

    [Fact]
    public async Task ARepeatedKey_ShouldGetTheFirstResponse_WithoutBeingHandledAgain()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        using var first = await SendOrderAsync(client, "key-1", "o1");
        using var repeated = await SendOrderAsync(client, "key-1", "o1");

        _orders.Placed.ShouldBe(["o1"]);
        repeated.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeated.Headers.Location.ShouldBe(first.Headers.Location);
        (await repeated.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        repeated.Headers.GetValues("Idempotency-Replayed").ShouldBe(["true"]);
        first.Headers.Contains("Idempotency-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task AKeyUsedForADifferentRequest_ShouldBeAnswered422()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        using var first = await SendOrderAsync(client, "key-1", "o1");

        using var different = await SendOrderAsync(client, "key-1", "o2");

        different.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        _orders.Placed.ShouldBe(["o1"]);
    }

    [Fact]
    public async Task ARepeatWhileTheFirstIsHandled_ShouldBeAnswered409()
    {
        _orders.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        var first = Task.Run(() => SendOrderAsync(app.GetTestClient(), "key-1", "o1"));
        try
        {
            await _orders.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var repeated = await SendOrderAsync(client, "key-1", "o1");

            repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            repeated.Headers.RetryAfter.ShouldNotBeNull();
        }
        finally
        {
            _orders.Hold.TrySetResult();
            using var firstResponse = await first;
            firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task AServerError_ShouldNotBeKept_SoTheClientCanTryAgain()
    {
        _orders.FailNext = true;
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        using var failed = await SendOrderAsync(client, "key-1", "o1");
        using var retried = await SendOrderAsync(client, "key-1", "o1");

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        retried.StatusCode.ShouldBe(HttpStatusCode.Created);
        _orders.Placed.ShouldBe(["o1"]);
    }

    [Fact]
    public async Task AHandlerThatThrew_ShouldNotKeepTheKey_SoTheClientCanTryAgain()
    {
        _orders.ThrowNext = true;
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        // The test server hands the handler's exception to the client.
        await Should.ThrowAsync<InvalidOperationException>(() => SendOrderAsync(client, "key-1", "o1"));
        using var retried = await SendOrderAsync(client, "key-1", "o1");

        retried.StatusCode.ShouldBe(HttpStatusCode.Created);
        _orders.Placed.ShouldBe(["o1"]);
    }

    [Fact]
    public async Task RequestsWithoutAKey_ShouldBeHandledAsUsual()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        using var first = await SendOrderAsync(client, key: null, "o1");
        using var second = await SendOrderAsync(client, key: null, "o1");

        _orders.Placed.ShouldBe(["o1", "o1"]);
    }

    [Fact]
    public async Task ARequiredKey_ShouldBeAnswered400_WhenMissing()
    {
        await using var app = await StartAsync(requireKey: true);

        using var response = await SendOrderAsync(app.GetTestClient(), key: null, "o1");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _orders.Placed.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSameKey_ShouldBeSeparatePerUser()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        using var han = await SendOrderAsync(client, "key-1", "o1", user: "han");
        using var leia = await SendOrderAsync(client, "key-1", "o1", user: "leia");

        _orders.Placed.ShouldBe(["o1", "o1"]);
        leia.Headers.Contains("Idempotency-Replayed").ShouldBeFalse();
    }

    private static async Task<HttpResponseMessage> SendOrderAsync(HttpClient client, string? key, string orderId, string? user = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/orders", UriKind.Relative))
        {
            Content = JsonContent.Create(new PlaceOrder(orderId)),
        };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        if (user is not null)
            request.Headers.Add("X-Test-User", user);
        return await client.SendAsync(request);
    }

    private async Task<WebApplication> StartAsync(bool requireKey = false)
        => await TestHosts.StartAsync(
            services => services
                .AddSingleton(_orders)
                .AddSingleton<InMemoryStore>()
                .AddSingleton<ICachingService>(provider => provider.GetRequiredService<InMemoryStore>())
                .AddSingleton<IClaimStore>(provider => provider.GetRequiredService<InMemoryStore>()),
            app =>
            {
                // Stands in for authentication: the user the test names.
                app.Use(async (context, next) =>
                {
                    if (context.Request.Headers["X-Test-User"] is [{ } user])
                        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user)],
                            "test"));
                    await next(context);
                });
                app.UseIdempotency();
                app.MapPost("/orders", async (PlaceOrder order, Orders orders) =>
                    {
                        if (!await orders.PlaceAsync(order.Id))
                            return Microsoft.AspNetCore.Http.Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
                        return Microsoft.AspNetCore.Http.Results.Created($"/orders/{order.Id}", new { order.Id, Number = orders.Placed.Length });
                    })
                    .WithIdempotency(requireKey);
            });

    internal sealed record PlaceOrder(string Id);

    internal sealed class Orders
    {
        private readonly ConcurrentQueue<string> _placed = new();

        public TaskCompletionSource? Hold { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailNext { get; set; }

        public bool ThrowNext { get; set; }

        public string[] Placed
            => [.. _placed];

        /// <summary>Places the order; <see langword="false"/> when it failed (<see cref="FailNext"/>).</summary>
        public async Task<bool> PlaceAsync(string id)
        {
            Started.TrySetResult();
            if (Hold is { } hold)
                await hold.Task;
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new InvalidOperationException("The warehouse is down.");
            }

            if (FailNext)
            {
                FailNext = false;
                return false;
            }

            _placed.Enqueue(id);
            return true;
        }
    }

    /// <summary>A cache and claim store in memory, without expiry.</summary>
    internal sealed class InMemoryStore : ICachingService, IClaimStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _claims = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, JsonSerializerOptions? serializerOptions = null, CancellationToken cancellationToken = default)
            where T : class
            => Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json, serializerOptions) : null);

        public Task SetAsync<T>(
            string key,
            T value,
            DateTimeOffset? absoluteExpiration,
            TimeSpan? absoluteExpirationRelativeToNow,
            TimeSpan? slidingExpiration,
            JsonSerializerOptions? serializerOptions,
            CancellationToken cancellationToken)
            where T : class
        {
            _values[key] = JsonSerializer.Serialize(value, serializerOptions);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<T?>> GetBulkAsync<T>(IEnumerable<string> keys, JsonSerializerOptions? serializerOptions = null, CancellationToken cancellationToken = default)
            => Task.FromResult(keys.Select(key => _values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json, serializerOptions) : default));

        public Task<bool> TryClaimForAsync(string key, TimeSpan expiresIn, CancellationToken cancellationToken = default)
            => Task.FromResult(_claims.TryAdd(key, 0));

        public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
        {
            _claims.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
