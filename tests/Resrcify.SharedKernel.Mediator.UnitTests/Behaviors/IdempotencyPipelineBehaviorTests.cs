using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Caching.Primitives;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Configuration;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

/// <summary>The idempotency behavior in the mediator's standard pipeline, on a cache and claim store in memory.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class IdempotencyPipelineBehaviorTests
{
    private readonly Handled _handled = new();
    private readonly InMemoryCacheAndClaims _store = new();

    [Fact]
    public async Task ARequestWithoutAKey_ShouldBeHandledEveryTime()
    {
        await using var services = Services();

        await SendAsync(services, new CreateShard("Main", Key: null));
        await SendAsync(services, new CreateShard("Main", Key: null));

        _handled.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ARepeatedKey_ShouldGetTheFirstResult_WithoutBeingHandledAgain_AndBeMarkedReplayed()
    {
        await using var services = Services();
        await using var scope = services.CreateAsyncScope();
        var first = new CreateShard("Main", "key-1");
        var repeat = new CreateShard("Main", "key-1");

        var firstResult = await scope.ServiceProvider.GetRequiredService<ISender>().Send(first);
        var repeated = await scope.ServiceProvider.GetRequiredService<ISender>().Send(repeat);

        _handled.Count.ShouldBe(1);
        repeated.Value.ShouldBe(firstResult.Value);
        var context = scope.ServiceProvider.GetRequiredService<IIdempotencyContext>();
        context.WasReplayed(repeat).ShouldBeTrue();
        context.WasReplayed(first).ShouldBeFalse();
    }

    [Fact]
    public async Task ARepeatedKey_ShouldGetTheFirstResult_ThroughTheDistributedCache()
    {
        // The Caching package's cache and claim store: the kept result round-trips through its JSON.
        var cache = new DistributedCachingService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        await using var services = Services(store: cache);

        var first = await SendAsync(services, new CreateShard("Main", "key-1"));
        var repeated = await SendAsync(services, new CreateShard("Main", "key-1"));
        var reused = await SendAsync(services, new CreateShard("Other", "key-1"));

        repeated.Value.ShouldBe(first.Value);
        reused.Errors.ShouldHaveSingleItem().ShouldBe(IdempotencyErrors.KeyReused);
        _handled.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AKeyRepeatedWithADifferentRequest_ShouldBeUnprocessable()
    {
        await using var services = Services();
        await SendAsync(services, new CreateShard("Main", "key-1"));

        var different = await SendAsync(services, new CreateShard("Other", "key-1"));

        different.Errors.ShouldHaveSingleItem().ShouldBe(IdempotencyErrors.KeyReused);
        different.Errors.Single().Type.ShouldBe(ErrorType.Unprocessable);
        _handled.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ARepeatWhileTheFirstIsHandled_ShouldBeAConflict()
    {
        await using var services = Services();
        _store.Claim($"idempotency:{typeof(CreateShard).FullName}::key-1:in-progress");

        var repeat = await SendAsync(services, new CreateShard("Main", "key-1"));

        repeat.Errors.ShouldHaveSingleItem().ShouldBe(IdempotencyErrors.InProgress);
        _handled.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ATransientFailure_ShouldNotBeKept_SoARepeatRunsAgain()
    {
        _handled.Answer = _ => Result.Failure<ShardView>(Error.Timeout("Upstream.Timeout", "Too slow."));
        await using var services = Services();

        await SendAsync(services, new CreateShard("Main", "key-1"));
        _handled.Answer = null;
        var retried = await SendAsync(services, new CreateShard("Main", "key-1"));

        retried.IsSuccess.ShouldBeTrue();
        _handled.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AFailureThatIsTheRequestsFault_ShouldBeKept()
    {
        _handled.Answer = _ => Result.Failure<ShardView>(Error.Conflict("Shard.Exists", "Taken."));
        await using var services = Services();

        await SendAsync(services, new CreateShard("Main", "key-1"));
        var repeated = await SendAsync(services, new CreateShard("Main", "key-1"));

        repeated.Errors.ShouldHaveSingleItem().Code.ShouldBe("Shard.Exists");
        _handled.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AHandlerThatThrew_ShouldLetTheKeyGo()
    {
        _handled.Answer = _ => throw new InvalidOperationException("The database is down.");
        await using var services = Services();

        await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(services, new CreateShard("Main", "key-1")));
        _handled.Answer = null;
        var retried = await SendAsync(services, new CreateShard("Main", "key-1"));

        retried.IsSuccess.ShouldBeTrue();
        _store.Claims.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("a-key-of-300-characters")]
    public async Task AnInvalidKey_ShouldBeAValidationFailure(string key)
    {
        await using var services = Services();
        if (key.StartsWith("a-key", StringComparison.Ordinal))
            key = new string('k', 300);

        var result = await SendAsync(services, new CreateShard("Main", key));

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("Idempotency.InvalidKey");
        _handled.Count.ShouldBe(0);
    }

    [Fact]
    public async Task TheSameKey_ShouldBeSeparatePerScope()
    {
        await using var services = Services();

        await SendAsync(services, new CreateShard("Main", "key-1") { Scope = "han" });
        await SendAsync(services, new CreateShard("Main", "key-1") { Scope = "leia" });

        _handled.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ACommandWithoutAValue_ShouldBeReplayedToo()
    {
        await using var services = Services();

        await SendAsync(services, new RenameShard("key-1"));
        var repeated = await SendAsync(services, new RenameShard("key-1"));

        repeated.IsSuccess.ShouldBeTrue();
        _handled.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AReplay_ShouldOpenNoTransaction()
    {
        // The behavior runs outside the transaction and the unit of work: the repeat never reaches them.
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Result<ShardView>>>>(),
                Arg.Any<IsolationLevel>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result<ShardView>>>>().Invoke(CancellationToken.None));
        unitOfWork.RunningOperations<Result<ShardView>>();
        await using var services = Services(unitOfWork);

        await SendAsync(services, new CreateShardInTransaction("key-1"));
        await SendAsync(services, new CreateShardInTransaction("key-1"));

        _handled.Count.ShouldBe(1);
        await unitOfWork.ReceivedWithAnyArgs(1).ExecuteInTransactionAsync<Result<ShardView>>(default!, default, default, default);
    }

    [Fact]
    public async Task AKeyWithoutACacheAndClaimStore_ShouldSaySoWhenSent()
    {
        await using var services = Services(withStore: false);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(services, new CreateShard("Main", "key-1")));

        failure.Message.ShouldContain("needs an ICachingService and an IClaimStore");
    }

    [Fact]
    public void BehaviorCheck_ShouldFailTheRegistration_WhenTheBehaviorIsLeftOutButARequestIsIdempotent()
        => Should.Throw<InvalidOperationException>(() => BehaviorCheck.RunFor(
                new ServiceCollection(),
                [(typeof(CreateShard), BehaviorCheck.PipelineKind.Task)],
                new MediatorConfiguration().AddStandardBehaviors(options => options.Without(StandardBehavior.Idempotency)).OpenBehaviorTypes))
            .Message.ShouldContain("IdempotencyPipelineBehavior");

    private static Task<TResponse> SendAsync<TResponse>(ServiceProvider services, IRequest<TResponse> request)
        => SendInScopeAsync(services, request);

    private static async Task<TResponse> SendInScopeAsync<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private ServiceProvider Services(IUnitOfWork? unitOfWork = null, bool withStore = true, DistributedCachingService? store = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_handled);
        services.AddScoped(_ => unitOfWork ?? Substitute.For<IUnitOfWork>().RunningOperations<Result<ShardView>>().RunningOperations<Result>());
        if (store is not null)
        {
            services.AddSingleton<ICachingService>(store);
            services.AddSingleton<IClaimStore>(store);
        }
        else if (withStore)
        {
            services.AddSingleton<ICachingService>(_store);
            services.AddSingleton<IClaimStore>(_store);
        }

        services.AddMediator(cfg => cfg.AddStandardBehaviors(options => options.Without(StandardBehavior.Caching)));
        // Open generic handlers, closed here: the assembly scan of other tests must not find an idempotent request.
        services.AddTransient<IRequestHandler<CreateShard, Result<ShardView>>, CreatingHandler<CreateShard>>();
        services.AddTransient<IRequestHandler<CreateShardInTransaction, Result<ShardView>>, CreatingHandler<CreateShardInTransaction>>();
        services.AddTransient<IRequestHandler<RenameShard, Result>, RenamingHandler<RenameShard>>();
        return services.BuildServiceProvider();
    }

    public sealed record ShardView(Guid Id, string Name);

    public sealed record CreateShard(string Name, string? Key) : ICommand<ShardView>, IIdempotentRequest
    {
        public string? Scope { get; init; }

        public string? IdempotencyKey
            => Key;

        public string? IdempotencyScope
            => Scope;
    }

    public sealed record CreateShardInTransaction(string? IdempotencyKey) : ITransactionCommand<ShardView>, IIdempotentRequest
    {
        public TimeSpan? CommandTimeout
            => null;

        public IsolationLevel? IsolationLevel
            => null;
    }

    public sealed record RenameShard(string? IdempotencyKey) : ICommand, IIdempotentRequest;

    public sealed class Handled
    {
        private int _count;

        public int Count
            => Volatile.Read(ref _count);

        public Func<object, Result<ShardView>>? Answer { get; set; }

        public Result<ShardView> Handle(object request)
        {
            Interlocked.Increment(ref _count);
            return Answer?.Invoke(request) ?? Result.Success(new ShardView(Guid.NewGuid(), "Main"));
        }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Resolved from the container.")]
    internal sealed class CreatingHandler<TRequest>(Handled handled) : IRequestHandler<TRequest, Result<ShardView>>
        where TRequest : IRequest<Result<ShardView>>
    {
        public Task<Result<ShardView>> Handle(TRequest request, CancellationToken cancellationToken)
            => Task.FromResult(handled.Handle(request));
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Resolved from the container.")]
    internal sealed class RenamingHandler<TRequest>(Handled handled) : IRequestHandler<TRequest, Result>
        where TRequest : IRequest<Result>
    {
        public Task<Result> Handle(TRequest request, CancellationToken cancellationToken)
        {
            handled.Handle(request);
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>A cache (through JSON, as a distributed one) and a claim store, in memory and without expiry.</summary>
    internal sealed class InMemoryCacheAndClaims : ICachingService, IClaimStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _claims = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Claims
            => [.. _claims.Keys];

        public void Claim(string key)
            => _claims[key] = 0;

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
