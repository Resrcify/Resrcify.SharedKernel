using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Messaging.Behaviors;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Behaviors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
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
    public async Task Handle_RunsHandlerInsideTransaction_AndReturnsItsResult()
    {
        // Arrange
        var result = Result.Success();
        _next.Invoke().Returns(result);
        ExecuteTransactionByRunningOperation();

        // Act
        var response = await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert — the handler ran through ExecuteInTransactionAsync.
        response.ShouldBe(result);
        await _next.Received(1).Invoke();
        await _unitOfWork
            .Received(1)
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Result>>>(),
                Arg.Any<IsolationLevel>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsFailureResult_WithoutThrowing()
    {
        // Arrange — a failure result is rolled back by the unit of work and handed back.
        var result = Result.Failure(Error.NullValue);
        _next.Invoke().Returns(result);
        ExecuteTransactionByRunningOperation();

        // Act
        var response = await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        response.ShouldBe(result);
    }

    [Fact]
    public async Task Handle_WrapsAndLogsError_WhenTransactionThrows()
    {
        // Arrange
        var exception = new InvalidOperationException("transaction failed");
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Result>>>(),
                Arg.Any<IsolationLevel>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw exception);

        // Act
        async Task Act() => await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        var thrown = await Should.ThrowAsync<InvalidOperationException>(Act);
        thrown.InnerException.ShouldBe(exception);
        _logger.Received(1).LogError(
            exception,
            "Exception caught in TransactionPipelineBehavior");
    }

    [Fact]
    public async Task Handle_UsesDefaultIsolationAndTimeout_WhenCommandLeavesThemNull()
    {
        // Arrange
        _command.CommandTimeout.Returns((TimeSpan?)null);
        _command.IsolationLevel.Returns((IsolationLevel?)null);
        _next.Invoke().Returns(Result.Success());
        ExecuteTransactionByRunningOperation();

        // Act
        await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        await _unitOfWork
            .Received(1)
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Result>>>(),
                IsolationLevel.ReadCommitted,
                TimeSpan.FromSeconds(30),
                Arg.Any<CancellationToken>());
    }

    // Makes the mocked unit of work actually run the handler delegate it is given.
    private void ExecuteTransactionByRunningOperation()
        => _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Result>>>(),
                Arg.Any<IsolationLevel>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                callInfo.Arg<Func<CancellationToken, Task<Result>>>()
                    .Invoke(CancellationToken.None));
}
