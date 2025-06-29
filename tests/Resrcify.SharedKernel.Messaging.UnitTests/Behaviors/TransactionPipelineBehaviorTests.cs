using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.Messaging.Behaviors;
using System.Data;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using System.Diagnostics.CodeAnalysis;
using Shouldly;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Behaviors;

public class TransactionPipelineBehaviorTests
{
    private readonly TransactionPipelineBehavior<ITransactionCommand, Result> _behavior;
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<TransactionPipelineBehavior<ITransactionCommand, Result>> _logger = Substitute.For<ILogger<TransactionPipelineBehavior<ITransactionCommand, Result>>>();
    private readonly RequestHandlerDelegate<Result> _next = Substitute.For<RequestHandlerDelegate<Result>>();
    private readonly ITransactionCommand _command = Substitute.For<ITransactionCommand>();

    public TransactionPipelineBehaviorTests()
        => _behavior = new TransactionPipelineBehavior<ITransactionCommand, Result>(_unitOfWork, _logger);

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
            .ShouldBe(result);

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
            .ShouldBe(result);

        await _unitOfWork
            .Received(1)
            .RollbackTransactionAsync(CancellationToken.None);
    }

    [Fact]
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "Exception type is not important in this context")]
    [SuppressMessage(
        "Sonar Bug",
        "S112:General exceptions should never be thrown",
        Justification = "Exception type is not important in this context")]
    public async Task Handle_ShouldRollbackTransactionAndLogError_OnException()
    {
        // Arrange
        var exception = new Exception();
        _next.When(n => n.Invoke()).Do(x => throw exception);

        // Act
        async Task act() => await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        await Should.ThrowAsync<Exception>(act);

        // Verify that the logger logs the error
        _logger.Received(1).LogError(
            exception,
            "Exception caught in TransactionPipelineBehavior");
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
            .ShouldBe(result);

        await _unitOfWork
            .Received(1)
            .BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
    }
}