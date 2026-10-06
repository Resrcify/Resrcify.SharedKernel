
using Xunit;
using System.Diagnostics.CodeAnalysis;
using NSubstitute;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;
using System.Threading;
using System;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using System.Linq;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class LoggingPipelineBehaviorTests
{
    private readonly ILogger<LoggingPipelineBehavior<MockRequest, Result>> _logger;
    private readonly LoggingPipelineBehavior<MockRequest, Result> _behavior;

    public LoggingPipelineBehaviorTests()
    {
        _logger = Substitute.For<ILogger<LoggingPipelineBehavior<MockRequest, Result>>>();
        _behavior = new LoggingPipelineBehavior<MockRequest, Result>(_logger);
    }

    [Fact]
    public async Task Handle_ShouldLogInformationAtStartAndCompletion()
    {
        // Arrange
        var request = new MockRequest();
        var response = Result.Success();
        var cancellationToken = CancellationToken.None;
        Task<Result> next(CancellationToken cancellationToken = default) => Task.FromResult(response);

        // Act
        await _behavior.Handle(request, next, cancellationToken);

        // Assert
        _logger
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .Count(callArguments => callArguments[0]!.Equals(LogLevel.Information))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Handle_ShouldLogTheFailureAsInformation_WhenItIsAboutTheRequest()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        static Task<Result> next(CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure(Error.NotFound("Company.NotFound", "No such company")));

        await _behavior.Handle(new MockRequest(), next, CancellationToken.None);

        LoggedLevels().ShouldBe([LogLevel.Information, LogLevel.Information, LogLevel.Information]);
    }

    [Fact]
    public async Task Handle_ShouldLogTheFailureAsWarning_WhenItIsTransient()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        static Task<Result> next(CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure(Error.ExternalFailure("Upstream.Down", "The upstream is down")));

        await _behavior.Handle(new MockRequest(), next, CancellationToken.None);

        LoggedLevels().ShouldBe([LogLevel.Information, LogLevel.Warning, LogLevel.Information]);
    }

    [Fact]
    public async Task Handle_ShouldLogStartAndCompletionAtTheConfiguredLevel_WhenItIsDebug()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var behavior = new LoggingPipelineBehavior<MockRequest, Result>(
            _logger,
            options: new LoggingPipelineOptions { RequestLevel = LogLevel.Debug });

        await behavior.Handle(new MockRequest(), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        LoggedLevels().ShouldBe([LogLevel.Debug, LogLevel.Debug]);
    }

    [Fact]
    public async Task Handle_ShouldLogAWarning_WhenTheRequestTakesLongerThanTheThreshold()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var clock = new FakeTimeProvider();
        var behavior = new LoggingPipelineBehavior<MockRequest, Result>(
            _logger,
            clock,
            new LoggingPipelineOptions { SlowRequestThreshold = TimeSpan.FromSeconds(1) });

        await behavior.Handle(
            new MockRequest(),
            _ =>
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                return Task.FromResult(Result.Success());
            },
            CancellationToken.None);

        LoggedLevels().ShouldBe([LogLevel.Information, LogLevel.Warning, LogLevel.Information]);
    }

    [Fact]
    public async Task Handle_ShouldNotWarn_WhenTheRequestIsWithinTheThreshold()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var clock = new FakeTimeProvider();
        var behavior = new LoggingPipelineBehavior<MockRequest, Result>(
            _logger,
            clock,
            new LoggingPipelineOptions { SlowRequestThreshold = TimeSpan.FromSeconds(1) });

        await behavior.Handle(
            new MockRequest(),
            _ =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(500));
                return Task.FromResult(Result.Success());
            },
            CancellationToken.None);

        LoggedLevels().ShouldBe([LogLevel.Information, LogLevel.Information]);
    }

    [Fact]
    public async Task Handle_ShouldLogTheExceptionAndLetItThrough_WhenTheHandlerThrows()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var thrown = new InvalidOperationException("The handler failed.");

        var caught = await Should.ThrowAsync<InvalidOperationException>(
            () => _behavior.Handle(new MockRequest(), _ => Task.FromException<Result>(thrown), CancellationToken.None));

        caught.ShouldBeSameAs(thrown);
        LoggedLevels().ShouldBe([LogLevel.Information, LogLevel.Error]);
    }

    [Fact]
    public async Task Handle_ShouldNotLogTheException_WhenTheCallerCancelled()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _behavior.Handle(
            new MockRequest(),
            token => Task.FromCanceled<Result>(token),
            cancellation.Token));

        LoggedLevels().ShouldBe([LogLevel.Information]);
    }

    /// <summary>The level of each entry logged (not the <c>IsEnabled</c> checks).</summary>
    private LogLevel[] LoggedLevels()
        => [.. _logger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => (LogLevel)call.GetArguments()[0]!)];

    [SuppressMessage(
        "Maintainability",
        "CA1515:Consider making public types internal",
        Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
    [SuppressMessage(
        "Performance",
        "CA1515:Consider making public types internal",
        Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
    public sealed class MockRequest : IRequest<Result> { }
}