using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

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
    public async Task Handle_ShouldRethrowTheOriginalExceptionAndLogIt_WhenTransactionThrows()
    {
        // Arrange
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
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
        thrown.ShouldBeSameAs(exception);
        LoggedEntries().ShouldBe([(LogLevel.Error, exception)]);
    }

    [Fact]
    public async Task Handle_ShouldLogAtDebugWithoutTheException_WhenABehaviorInsideAlreadyLoggedIt()
    {
        // Arrange — the unit-of-work behavior inside this one logged it first.
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var exception = new InvalidOperationException("transaction failed");
        LoggedExceptions.Claim(exception);
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
        (await Should.ThrowAsync<InvalidOperationException>(Act)).ShouldBeSameAs(exception);
        LoggedEntries().ShouldBe([(LogLevel.Debug, (Exception?)null)]);
    }

    [Fact]
    public async Task Handle_ShouldRethrowWithoutLogging_WhenTheCallerCancelled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<Result>>>(),
                Arg.Any<IsolationLevel>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new OperationCanceledException(cancellation.Token));

        // Act
        async Task Act() => await _behavior.Handle(_command, _next, cancellation.Token);

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(Act);
        _logger.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_UsesReadCommittedAndTheContextsTimeout_WhenCommandLeavesThemNull()
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
                null,
                Arg.Any<CancellationToken>());
    }

    /// <summary>The level and exception of each entry logged (not the <c>IsEnabled</c> checks).</summary>
    private (LogLevel Level, Exception? Exception)[] LoggedEntries()
        => [.. _logger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => ((LogLevel)call.GetArguments()[0]!, (Exception?)call.GetArguments()[3]))];

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
