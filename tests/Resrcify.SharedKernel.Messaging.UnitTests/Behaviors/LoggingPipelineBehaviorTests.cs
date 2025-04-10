
using Xunit;
using NSubstitute;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using MediatR;
using FluentAssertions;
using System;
using Resrcify.SharedKernel.Messaging.Behaviors;
using System.Linq;
using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Behaviors;

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
        RequestHandlerDelegate<Result> next = (cancellationToken) => Task.FromResult(response);

        // Act
        var result = await _behavior.Handle(request, next, cancellationToken);

        // Assert
        _logger
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .Count(callArguments => callArguments[0]!.Equals(LogLevel.Information))
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task Handle_WhenResponseIndicatesFailure_ShouldLogErrorInformation()
    {
        // Arrange
        var request = new MockRequest();
        var response = Result.Failure(Error.NullValue);
        var cancellationToken = CancellationToken.None;
        RequestHandlerDelegate<Result> next = (cancellationToken) => Task.FromResult(response);

        // Act
        var result = await _behavior.Handle(request, next, cancellationToken);

        // Assert
        _logger
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .Count(callArguments => callArguments[0]!.Equals(LogLevel.Information))
            .Should()
            .Be(3);
    }

    public class MockRequest : IRequest<Result> { }
}