using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using System.Diagnostics.CodeAnalysis;
using Shouldly;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class UnitOfWorkPipelineBehaviorTests
{
    private readonly UnitOfWorkPipelineBehavior<ICommand, Result> _behavior;
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>().RunningOperations<Result>();
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
    [SuppressMessage(
        "Sonar Bug",
        "S112:General exceptions should never be thrown",
        Justification = "Exception type is not important in this context")]
    public async Task Handle_ShouldRethrowTheOriginalExceptionAndLogIt_WhenTheHandlerThrows()
    {
        // Arrange
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var exception = new Exception();
        _next.When(n => n.Invoke()).Do(_ => throw exception);

        // Act
        async Task act() => await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        var thrown = await Should.ThrowAsync<Exception>(act);
        thrown.ShouldBeSameAs(exception);

        // Verify that the logger logs the error, once, with the exception
        LoggedEntries().ShouldBe([(LogLevel.Error, exception)]);
    }

    [Fact]
    public async Task Handle_ShouldLogAtDebugWithoutTheException_WhenItWasAlreadyLogged()
    {
        // Arrange — a request sent by the handler logged it already.
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var exception = new InvalidOperationException("nested request failed");
        LoggedExceptions.Claim(exception);
        _next.When(n => n.Invoke()).Do(_ => throw exception);

        // Act
        async Task Act() => await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        (await Should.ThrowAsync<InvalidOperationException>(Act)).ShouldBeSameAs(exception);
        LoggedEntries().ShouldBe([(LogLevel.Debug, (Exception?)null)]);
    }

    /// <summary>The level and exception of each entry logged (not the <c>IsEnabled</c> checks).</summary>
    private (LogLevel Level, Exception? Exception)[] LoggedEntries()
        => [.. _logger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => ((LogLevel)call.GetArguments()[0]!, (Exception?)call.GetArguments()[3]))];

    [Fact]
    public async Task Handle_ShouldReturnThePersistenceFailure_WhenAskedTo_AndTheSaveConflicts()
    {
        // Arrange
        var conflict = Error.Conflict("Persistence.Concurrency", "stale");
        _unitOfWork.TryCompleteAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(conflict));
        var behavior = new UnitOfWorkPipelineBehavior<ICommand, Result>(
            _unitOfWork,
            _logger,
            new UnitOfWorkPipelineOptions { ReturnPersistenceFailures = true });
        _next.Invoke(Arg.Any<CancellationToken>()).Returns(Result.Success());

        // Act
        var response = await behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        response.Errors.ShouldHaveSingleItem().ShouldBe(conflict);
        await _unitOfWork.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnThePersistenceFailureAsTheCommandsResultType_ForACommandWithAValue()
    {
        // Arrange
        var conflict = Error.Conflict("Persistence.UniqueViolation", "taken");
        var unitOfWork = Substitute.For<IUnitOfWork>().RunningOperations<Result<Guid>>();
        unitOfWork.TryCompleteAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure(conflict));
        var behavior = new UnitOfWorkPipelineBehavior<ICommand<Guid>, Result<Guid>>(
            unitOfWork,
            Substitute.For<ILogger<UnitOfWorkPipelineBehavior<ICommand<Guid>, Result<Guid>>>>(),
            new UnitOfWorkPipelineOptions { ReturnPersistenceFailures = true });
        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();
        next.Invoke(Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.NewGuid()));

        // Act
        var response = await behavior.Handle(Substitute.For<ICommand<Guid>>(), next, CancellationToken.None);

        // Assert
        response.IsFailure.ShouldBeTrue();
        response.Errors.ShouldHaveSingleItem().ShouldBe(conflict);
    }

    [Fact]
    public async Task Handle_ShouldReturnTheHandlersResponse_WhenAskedTo_AndTheSaveSucceeds()
    {
        // Arrange
        _unitOfWork.TryCompleteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());
        var behavior = new UnitOfWorkPipelineBehavior<ICommand, Result>(
            _unitOfWork,
            _logger,
            new UnitOfWorkPipelineOptions { ReturnPersistenceFailures = true });
        var result = Result.Success();
        _next.Invoke(Arg.Any<CancellationToken>()).Returns(result);

        // Act
        var response = await behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        response.ShouldBeSameAs(result);
        await _unitOfWork.Received(1).TryCompleteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_ShouldRunTheCommandThroughExecuteAsync_SoWhatAFailureChangedIsUndone()
    {
        // Arrange
        var failure = Result.Failure(Error.NullValue);
        _next.Invoke(Arg.Any<CancellationToken>()).Returns(failure);

        // Act
        var response = await _behavior.Handle(_command, _next, CancellationToken.None);

        // Assert
        response.ShouldBe(failure);
        await _unitOfWork.Received(1).ExecuteAsync(
            Arg.Any<Func<CancellationToken, Task<Result>>>(),
            CancellationToken.None);
        await _unitOfWork.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldLeaveATransactionalCommandsException_ToTheTransactionBehaviorAroundIt()
    {
        // Arrange — under a retrying strategy this behavior runs once per attempt; a retried attempt isn't an error.
        var logger = Substitute.For<ILogger<UnitOfWorkPipelineBehavior<ITransactionCommand, Result>>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var behavior = new UnitOfWorkPipelineBehavior<ITransactionCommand, Result>(_unitOfWork, logger);
        var exception = new InvalidOperationException("attempt failed");
        _next.When(n => n.Invoke(Arg.Any<CancellationToken>())).Do(_ => throw exception);

        // Act
        async Task Act() => await behavior.Handle(Substitute.For<ITransactionCommand>(), _next, CancellationToken.None);

        // Assert
        (await Should.ThrowAsync<InvalidOperationException>(Act)).ShouldBeSameAs(exception);
        LoggedExceptions.IsLogged(exception).ShouldBeFalse();
        logger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => (LogLevel)call.GetArguments()[0]!)
            .ShouldBe([LogLevel.Debug]);
    }

    [Fact]
    public async Task Handle_ShouldRethrowWithoutLogging_WhenTheCallerCancelled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _next.When(n => n.Invoke(Arg.Any<CancellationToken>()))
            .Do(_ => throw new OperationCanceledException(cancellation.Token));

        // Act
        async Task Act() => await _behavior.Handle(_command, _next, cancellation.Token);

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(Act);
        _logger.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }
}
