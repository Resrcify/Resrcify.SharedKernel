
using Xunit;
using NSubstitute;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using MediatR;
using System;
using Resrcify.SharedKernel.Messaging.Behaviors;
using System.Linq;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Shouldly;

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
    public async Task Handle_WhenResponseIndicatesFailure_ShouldLogErrorInformation()
    {
        // Arrange
        var request = new MockRequest();
        var response = Result.Failure(Error.NullValue);
        var cancellationToken = CancellationToken.None;
        Task<Result> next(CancellationToken cancellationToken = default) => Task.FromResult(response);

        // Act
        await _behavior.Handle(request, next, cancellationToken);

        // Assert
        _logger
            .ReceivedCalls()
            .Select(call => call.GetArguments())
            .Count(callArguments => callArguments[0]!.Equals(LogLevel.Information))
            .ShouldBe(3);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "CA1515:Consider making public types internal",
        Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
    public sealed class MockRequest : IRequest<Result> { }
}