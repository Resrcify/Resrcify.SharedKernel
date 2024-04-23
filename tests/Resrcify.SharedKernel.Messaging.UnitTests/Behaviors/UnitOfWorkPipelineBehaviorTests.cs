using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.GenericUnitOfWork.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Shared;
using Resrcify.SharedKernel.Messaging.Behaviors;

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
            .Should()
            .Be(result);

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
            .Should()
            .Be(result);

        await _unitOfWork
            .DidNotReceive()
            .CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ThrowsException_AndLogsError()
    {
        // Arrange
        var exception = new Exception("Test exception");
        _next.When(n => n.Invoke()).Do(x => throw exception);

        // Act
        Func<Task> act = async () => await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        await act
            .Should()
            .ThrowAsync<Exception>()
            .WithMessage("Test exception");

        _logger.Received(1);
    }
}