using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
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
        await _cachingService.SetAsync(
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
        capturedOptions!.AbsoluteExpiration.ShouldBe(absoluteExpiration);
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
        capturedOptions!.AbsoluteExpiration.ShouldBe(absoluteExpiration);
        capturedOptions.AbsoluteExpirationRelativeToNow.ShouldBeNull();
    }

    [Fact]
    public async Task SetAsync_WithSlidingExpirationOverload_ShouldSetSlidingExpiration()
    {
        var key = "sliding-key";
        var obj = new Adress("Test", 123);
        var slidingExpiration = TimeSpan.FromMinutes(5);
        DistributedCacheEntryOptions? capturedOptions = null;

        _mockCache.WhenForAnyArgs(x => x.SetAsync(key, null!, null!, default))
            .Do(info => capturedOptions = info.Arg<DistributedCacheEntryOptions>());

        await ((ICachingService)_cachingService).SetAsync(key, obj, slidingExpiration);

        capturedOptions.ShouldNotBeNull();
        capturedOptions!.SlidingExpiration.ShouldBe(slidingExpiration);
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
}