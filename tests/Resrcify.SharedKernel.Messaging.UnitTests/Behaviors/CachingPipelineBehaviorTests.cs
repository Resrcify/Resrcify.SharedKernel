using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Resrcify.SharedKernel.Caching.Abstractions;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.Messaging.Behaviors;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Behaviors;

public class CachingPipelineBehaviorTests
{
    private readonly ICachingService _cachingService = Substitute.For<ICachingService>();
    private readonly ILogger<CachingPipelineBehavior<MockCachingQuery, Result>> _logger = Substitute.For<ILogger<CachingPipelineBehavior<MockCachingQuery, Result>>>();
    private readonly CachingPipelineBehavior<MockCachingQuery, Result> _behavior;

    public CachingPipelineBehaviorTests()
        => _behavior = new CachingPipelineBehavior<MockCachingQuery, Result>(_cachingService, _logger);

    [Fact]
    public async Task Handle_ShouldProceedToNext_WhenCacheKeyIsEmpty()
    {
        // Arrange
        var request = new MockCachingQuery("", TimeSpan.FromMinutes(60));
        var response = Result.Success();
        var cancellationToken = CancellationToken.None;
        RequestHandlerDelegate<Result> next = (cancellationToken) => Task.FromResult(response);

        _cachingService.GetAsync<Result>(Arg.Any<string>(), null, Arg.Any<CancellationToken>())!
                        .Returns(Task.FromResult<Result>(null!));

        // Act
        var result = await _behavior.Handle(request, next, cancellationToken);

        // Assert
        await _cachingService.DidNotReceiveWithAnyArgs().GetAsync<Result>(null!, null, cancellationToken);

        _logger
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .Count(callArguments => callArguments[0]!.Equals(LogLevel.Information))
            .Should()
            .Be(1);

        result
            .Should()
            .BeSameAs(response);
    }

    [Fact]
    public async Task Handle_ShouldReturnCachedResponse_WhenCacheHitOccurs()
    {
        //Arrange
        var request = new MockCachingQuery("valid-key", TimeSpan.FromMinutes(60));
        var response = Result.Success();
        var cancellationToken = CancellationToken.None;
        RequestHandlerDelegate<Result> next = (cancellationToken) => Task.FromResult(response);
        _cachingService.GetAsync<Result>(request.CacheKey!, null, Arg.Any<CancellationToken>())!.Returns(Task.FromResult(response));

        // Act
        var result = await _behavior.Handle(request, next, cancellationToken);

        //Assert
        _logger
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .Count(callArguments => callArguments[0]!.Equals(LogLevel.Information))
            .Should()
            .Be(1);

        result
            .Should()
            .BeEquivalentTo(response, options => options.ComparingByMembers<Result>());
    }

    [Fact]
    public async Task Handle_ShouldCacheResponse_WhenCacheMissAndSuccessfulResult()
    {
        //Arrange
        var request = new MockCachingQuery("valid-key", TimeSpan.FromMinutes(60));
        var response = Result.Success();
        var cancellationToken = CancellationToken.None;
        RequestHandlerDelegate<Result> next = (cancellationToken) => Task.FromResult(response);

        _cachingService.GetAsync<Result>(request.CacheKey!, null, Arg.Any<CancellationToken>())!.Returns(Task.FromResult<Result>(null!));

        //Act
        var result = await _behavior.Handle(request, next, cancellationToken);

        //Assert
        await _cachingService
            .Received(1)
            .GetAsync<Result>(request.CacheKey!, null, cancellationToken);

        await _cachingService
            .Received(1)
            .SetAsync(request.CacheKey!, response, request.Expiration, null, Arg.Any<CancellationToken>());

        _logger
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .Count(callArguments => callArguments[0]!.Equals(LogLevel.Information))
            .Should()
            .Be(1);

        result
            .Should()
            .Be(response);
    }

    public class MockCachingQuery(string? CacheKey, TimeSpan Expiration) : ICachingQuery<MockCachingQuery>
    {
        public string? CacheKey { get; set; } = CacheKey;
        public TimeSpan Expiration { get; set; } = Expiration;
    }
}