using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Caching.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Caching.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class DistributedCachingServiceTests
{
    private sealed record Adress(string Name, int PostNumber);
    private readonly IDistributedCache _mockCache = Substitute.For<IDistributedCache>();
    private readonly DistributedCachingService _cachingService;

    public DistributedCachingServiceTests()
        => _cachingService = new DistributedCachingService(_mockCache);

    [Fact]
    public async Task GetAsync_ShouldReturnDeserializedObject_WhenDataExists()
    {
        // Arrange
        var key = "test-key";
        var expectedObject = new Adress("Test", 123);
        var serializedData = JsonSerializer.SerializeToUtf8Bytes(expectedObject);
        _mockCache.GetAsync(key, Arg.Any<CancellationToken>()).Returns(serializedData);

        // Act
        var result = await _cachingService.GetAsync<Adress>(key);

        // Assert
        result?
            .ShouldNotBeNull();
        result
            .ShouldBe(expectedObject);
    }

    [Fact]
    public async Task SetAsync_ShouldSerializeAndSetDataWithExpiration()
    {
        // Arrange
        var key = "test-key";
        var obj = new Adress("Test", 123);
        var expiration = TimeSpan.FromMinutes(60);
        byte[]? capturedBytes = null;
        DistributedCacheEntryOptions? capturedOptions = null;

        _mockCache.WhenForAnyArgs(x => x.SetAsync(key, null!, null!, default))
            .Do(info =>
            {
                capturedBytes = info.Arg<byte[]>();
                capturedOptions = info.Arg<DistributedCacheEntryOptions>();
            });

        // Act
        await ((ICachingService)_cachingService).SetSlidingAsync(
            key,
            obj,
            slidingExpiration: expiration);

        // Assert
        // Verify SetAsync was called once
        await _mockCache.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());

        // Deserialize the captured byte[] to dynamic and compare the Name property using FluentAssertions
        var deserializedObject = JsonSerializer.Deserialize<Adress>(capturedBytes);
        deserializedObject
            .ShouldBe(obj);

        // Check SlidingExpiration using FluentAssertions
        capturedOptions!.SlidingExpiration
            .ShouldBe(expiration);
    }

    [Fact]
    public async Task SetAsync_WithAbsoluteExpirationOverload_ShouldSetAbsoluteExpiration()
    {
        var key = "absolute-key";
        var obj = new Adress("Test", 123);
        var absoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(30);
        DistributedCacheEntryOptions? capturedOptions = null;

        _mockCache.WhenForAnyArgs(x => x.SetAsync(key, null!, null!, default))
            .Do(info => capturedOptions = info.Arg<DistributedCacheEntryOptions>());

        await ((ICachingService)_cachingService).SetAsync(key, obj, absoluteExpiration);

        capturedOptions.ShouldNotBeNull();
        capturedOptions.AbsoluteExpiration.ShouldBe(absoluteExpiration);
        capturedOptions.AbsoluteExpirationRelativeToNow.ShouldBeNull();
    }

    [Fact]
    public async Task SetAsync_WithRelativeDurationAsDateTimeOffset_ShouldSetAbsoluteExpiration()
    {
        var key = "absolute-relative-key";
        var obj = new Adress("Test", 123);
        var absoluteRelativeExpiration = TimeSpan.FromMinutes(20);
        var absoluteExpiration = DateTimeOffset.UtcNow.Add(absoluteRelativeExpiration);
        DistributedCacheEntryOptions? capturedOptions = null;

        _mockCache.WhenForAnyArgs(x => x.SetAsync(key, null!, null!, default))
            .Do(info => capturedOptions = info.Arg<DistributedCacheEntryOptions>());

        await ((ICachingService)_cachingService).SetAsync(key, obj, absoluteExpiration);

        capturedOptions.ShouldNotBeNull();
        capturedOptions.AbsoluteExpiration.ShouldBe(absoluteExpiration);
        capturedOptions.AbsoluteExpirationRelativeToNow.ShouldBeNull();
    }

    [Fact]
    public async Task SetSlidingAsync_ShouldSetOnlyASlidingExpiration()
    {
        var key = "sliding-key";
        var obj = new Adress("Test", 123);
        var slidingExpiration = TimeSpan.FromMinutes(5);
        DistributedCacheEntryOptions? capturedOptions = null;

        _mockCache.WhenForAnyArgs(x => x.SetAsync(key, null!, null!, default))
            .Do(info => capturedOptions = info.Arg<DistributedCacheEntryOptions>());

        await ((ICachingService)_cachingService).SetSlidingAsync(key, obj, slidingExpiration);

        capturedOptions.ShouldNotBeNull();
        capturedOptions.SlidingExpiration.ShouldBe(slidingExpiration);
        capturedOptions.AbsoluteExpiration.ShouldBeNull();
        capturedOptions.AbsoluteExpirationRelativeToNow.ShouldBeNull();
    }

    [Fact]
    public async Task RemoveAsync_ShouldInvokeRemoveOnCache()
    {
        // Arrange
        var key = "test-key";

        // Act
        await _cachingService.RemoveAsync(key);

        // Assert
        await _mockCache
            .Received(1)
            .RemoveAsync(key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBulkAsync_ShouldReturnDeserializedObjects()
    {
        // Arrange
        var keys = new[] { "key1", "key2" };
        var expectedObjects = new[]
        {
            new Adress("Test", 123),
            new Adress("Test", 123)
        };

        var serializedData1 = JsonSerializer.SerializeToUtf8Bytes(expectedObjects[0]);
        var serializedData2 = JsonSerializer.SerializeToUtf8Bytes(expectedObjects[1]);

        _mockCache.GetAsync("key1", Arg.Any<CancellationToken>()).Returns(serializedData1);
        _mockCache.GetAsync("key2", Arg.Any<CancellationToken>()).Returns(serializedData2);

        // Act
        var results = await _cachingService.GetBulkAsync<Adress>(keys);
        var adressList = results.ToList();

        // Assert
        adressList
            .ShouldContain(expectedObjects[0]);
        adressList
            .ShouldContain(expectedObjects[1]);
        adressList.Count
            .ShouldBe(2);
    }

    [Fact]
    public async Task GetBulkAsync_ShouldReturnNullForMissingCacheEntries()
    {
        var keys = new[] { "key1", "key2" };
        var expectedObject = new Adress("Test", 123);

        var serializedData = JsonSerializer.SerializeToUtf8Bytes(expectedObject);

        _mockCache.GetAsync("key1", Arg.Any<CancellationToken>()).Returns(serializedData);
        _mockCache.GetAsync("key2", Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        var results = (await _cachingService.GetBulkAsync<Adress>(keys)).ToList();

        results.Count.ShouldBe(2);
        results[0].ShouldBe(expectedObject);
        results[1].ShouldBeNull();
    }

    [Fact]
    public async Task GetBulkAsync_ShouldProcessLargeKeySets()
    {
        var keys = Enumerable
            .Range(1, 300)
            .Select(index => $"key{index}")
            .ToArray();

        foreach (var key in keys)
        {
            var obj = new Adress(key, 123);
            _mockCache.GetAsync(key, Arg.Any<CancellationToken>())
                .Returns(JsonSerializer.SerializeToUtf8Bytes(obj));
        }

        var results = (await _cachingService.GetBulkAsync<Adress>(keys)).ToList();

        results.Count.ShouldBe(300);
        results.ShouldAllBe(result => result != null);

        await _mockCache
            .Received(300)
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetForAsync_ShouldPassTheDurationToTheCache_WhenGivenADuration()
    {
        // The cache measures it on its own clock (Redis' or the memory cache's), so no clock is read here.
        var obj = new Adress("Test", 123);
        DistributedCacheEntryOptions? capturedOptions = null;
        _mockCache.WhenForAnyArgs(x => x.SetAsync("for-key", null!, null!, default))
            .Do(info => capturedOptions = info.Arg<DistributedCacheEntryOptions>());

        await ((ICachingService)_cachingService).SetForAsync("for-key", obj, TimeSpan.FromMinutes(5));

        capturedOptions.ShouldNotBeNull();
        capturedOptions.AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromMinutes(5));
        capturedOptions.AbsoluteExpiration.ShouldBeNull();
        capturedOptions.SlidingExpiration.ShouldBeNull();
    }

    [Fact]
    public async Task SetForAsync_ShouldThrow_WhenTheDurationIsNotPositive()
        => await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => ((ICachingService)_cachingService).SetForAsync("for-key", new Adress("Test", 123), TimeSpan.Zero));

    [Fact]
    public async Task SetAsync_ShouldThrowAndCacheNothing_WhenGivenNoExpiration()
    {
        await Should.ThrowAsync<ArgumentException>(() => _cachingService.SetAsync(
            "kept-key",
            new Adress("Test", 123),
            absoluteExpiration: null,
            absoluteExpirationRelativeToNow: null,
            slidingExpiration: null,
            serializerOptions: null,
            cancellationToken: CancellationToken.None));

        await _mockCache.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SetForAsync_ShouldThrow_WhenTheLifetimeRunsPastTheLastDate()
        => await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => ((ICachingService)_cachingService).SetForAsync("for-key", new Adress("Test", 123), TimeSpan.MaxValue));

    [Fact]
    public async Task SetForAsync_ShouldSerializeWithTheCallersOptions_WhenGivenOptions()
    {
        // Arrange: indentation and escaping are the writer's, so a writer made without the options would drop them.
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var obj = new Adress("<Main & Co>", 123);
        byte[]? capturedBytes = null;
        _mockCache.WhenForAnyArgs(x => x.SetAsync("options-key", null!, null!, default))
            .Do(info => capturedBytes = info.Arg<byte[]>());

        // Act
        await ((ICachingService)_cachingService).SetForAsync("options-key", obj, TimeSpan.FromMinutes(5), options);

        // Assert
        capturedBytes.ShouldBe(JsonSerializer.SerializeToUtf8Bytes(obj, options));
        var json = Encoding.UTF8.GetString(capturedBytes!);
        json.ShouldContain(Environment.NewLine);
        json.ShouldContain("<Main & Co>");
    }

    [Fact]
    public async Task TryClaimForAsync_ShouldClaimOnlyOnce_WhenManyClaimTheSameKeyAtOnce()
    {
        // Arrange
        var cachingService = new DistributedCachingService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        using var start = new ManualResetEventSlim();

        // Act
        var claims = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                return await cachingService.TryClaimForAsync("claim-key", TimeSpan.FromMinutes(5));
            }))
            .ToList();
        start.Set();
        var claimed = await Task.WhenAll(claims);

        // Assert
        claimed.Count(won => won).ShouldBe(1);
    }

    [Fact]
    public async Task TryClaimForAsync_ShouldClaimAgain_WhenTheClaimWasRemoved()
    {
        // Arrange
        var cachingService = new DistributedCachingService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        (await cachingService.TryClaimForAsync("claim-key", TimeSpan.FromMinutes(5))).ShouldBeTrue();
        (await cachingService.TryClaimForAsync("claim-key", TimeSpan.FromMinutes(5))).ShouldBeFalse();

        // Act
        await cachingService.RemoveAsync("claim-key");

        // Assert
        (await cachingService.TryClaimForAsync("claim-key", TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task TryClaimForAsync_ShouldExpireTheClaimAfterTheLifetime_WhenItClaims()
    {
        // Arrange
        DistributedCacheEntryOptions? capturedOptions = null;
        _mockCache.GetAsync("claim-key", Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        _mockCache.WhenForAnyArgs(x => x.SetAsync("claim-key", null!, null!, default))
            .Do(info => capturedOptions = info.Arg<DistributedCacheEntryOptions>());

        // Act
        var claimed = await _cachingService.TryClaimForAsync("claim-key", TimeSpan.FromMinutes(5));

        // Assert
        claimed.ShouldBeTrue();
        capturedOptions.ShouldNotBeNull();
        capturedOptions.AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromMinutes(5));
        capturedOptions.SlidingExpiration.ShouldBeNull();
    }

    [Fact]
    public async Task TryClaimForAsync_ShouldThrow_WhenTheLifetimeIsNotPositive()
        => await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => _cachingService.TryClaimForAsync("claim-key", TimeSpan.Zero));
}
