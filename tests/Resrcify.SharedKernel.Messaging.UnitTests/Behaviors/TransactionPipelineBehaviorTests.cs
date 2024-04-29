using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Shared;
using Resrcify.SharedKernel.Messaging.Behaviors;
using System.Data;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Behaviors;

public class TransactionPipelineBehaviorTests
{
    private readonly TransactionPipelineBehavior<ITransactionalCommand, Result> _behavior;
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<TransactionPipelineBehavior<ITransactionalCommand, Result>> _logger = Substitute.For<ILogger<TransactionPipelineBehavior<ITransactionalCommand, Result>>>();
    private readonly RequestHandlerDelegate<Result> _next = Substitute.For<RequestHandlerDelegate<Result>>();
    private readonly ITransactionalCommand _command = Substitute.For<ITransactionalCommand>();

    public TransactionPipelineBehaviorTests()
    {
        _behavior = new TransactionPipelineBehavior<ITransactionalCommand, Result>(_unitOfWork, _logger);
    }

    [Fact]
    public async Task Handle_ShouldCommitTransaction_WhenResultIsSuccess()
    {
        // Arrange
        var result = Result.Success();
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
            .Received(1)
            .BeginTransactionAsync(Arg.Any<IsolationLevel>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

        await _unitOfWork
            .Received(1)
            .CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRollbackTransaction_WhenResultIsFailure()
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
            .Received(1)
            .RollbackTransactionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ShouldRollbackTransactionAndLogError_OnException()
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

        await _unitOfWork
            .Received(1)
            .RollbackTransactionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_UsesDefaultCommandTimeoutAndIsolationLevel_WhenNull()
    {
        // Arrange
        _command.CommandTimeout.Returns((TimeSpan?)null);
        _command.IsolationLevel.Returns((IsolationLevel?)null);
        var result = Result.Success();
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
            .Received(1)
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
    }
}