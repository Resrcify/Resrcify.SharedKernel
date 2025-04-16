using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.Messaging.Behaviors;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using System.Diagnostics.CodeAnalysis;
using Shouldly;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Behaviors;

public class UnitOfWorkPipelineBehaviorTests
{
    private readonly UnitOfWorkPipelineBehavior<ICommand, Result> _behavior;
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<UnitOfWorkPipelineBehavior<ICommand, Result>> _logger = Substitute.For<ILogger<UnitOfWorkPipelineBehavior<ICommand, Result>>>();
    private readonly RequestHandlerDelegate<Result> _next = Substitute.For<RequestHandlerDelegate<Result>>();
    private readonly ICommand _command = Substitute.For<ICommand>();

    public UnitOfWorkPipelineBehaviorTests()
        => _behavior = new UnitOfWorkPipelineBehavior<ICommand, Result>(_unitOfWork, _logger);

    [Fact]
    public async Task Handle_CompletesUnitOfWork_WhenResultIsSuccess()
    {
        // Arrange
        var result = Result.Success();
        _next.Invoke().Returns(result);

        // Act
        var response = await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        response
            .ShouldBe(result);

        await _unitOfWork
            .Received(1)
            .CompleteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_DoesNotCompleteUnitOfWork_WhenResultIsFailure()
    {
        // Arrange
        var result = Result.Failure(Error.NullValue);
        _next
            .Invoke()
            .Returns(result);

        // Act
        var response = await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        response
            .ShouldBe(result);

        await _unitOfWork
            .DidNotReceive()
            .CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "Exception type is not important in this context")]
    public async Task Handle_ThrowsException_AndLogsError()
    {
        // Arrange
        var exception = new Exception();
        _next.When(n => n.Invoke()).Do(x => throw exception);

        // Act
        async Task act() => await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert,
        var assertedException = await Should.ThrowAsync<Exception>(act);
        assertedException.Message.ShouldBe("An error occurred while processing the UnitOfWorkPipelineBehavior.");

        _logger.ReceivedCalls().Equals(1);
    }
}