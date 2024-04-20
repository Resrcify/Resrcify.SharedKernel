using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;
using Resrcify.SharedKernel.Caching.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.Caching.UnitTests.Primitives;

public class InMemoryCachingServiceTests
{
    private record Adress(string Name, int PostNumber);
    private readonly IDistributedCache _mockCache = Substitute.For<IDistributedCache>();
    private readonly InMemoryCachingService _cachingService;

    public InMemoryCachingServiceTests()
        => _cachingService = new InMemoryCachingService(_mockCache);

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
            .Should()
            .NotBeNull();
        result
            .Should()
            .Be(expectedObject);
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
        await _cachingService.SetAsync(key, obj, expiration);

        // Assert
        // Verify SetAsync was called once
        await _mockCache.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());

        // Deserialize the captured byte[] to dynamic and compare the Name property using FluentAssertions
        var deserializedObject = JsonSerializer.Deserialize<Adress>(capturedBytes);
        deserializedObject
            .Should()
            .Be(obj);

        // Check SlidingExpiration using FluentAssertions
        capturedOptions!.SlidingExpiration
            .Should()
            .Be(expiration);
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
            .Should()
            .Contain(expectedObjects[0]);
        adressList
            .Should()
            .Contain(expectedObjects[1]);
        adressList
            .Should()
            .HaveCount(2);
    }
}