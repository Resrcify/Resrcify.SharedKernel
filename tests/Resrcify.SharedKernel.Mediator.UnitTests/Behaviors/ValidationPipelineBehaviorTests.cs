using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class ValidationPipelineBehaviorTests
{
    private static ValidationPipelineBehavior<MockResultRequest, Result> CreateResultBehavior(IEnumerable<IValidator<MockResultRequest>> validators)
        => new(validators);
    private static ValidationPipelineBehavior<MockResultPrimitiveRequest, Result<int>> CreateResultPrimitiveBehavior(IEnumerable<IValidator<MockResultPrimitiveRequest>> validators)
        => new(validators);
    private static ValidationPipelineBehavior<MockResultReferenceRequest, Result<Response>> CreateResultReferenceBehavior(IEnumerable<IValidator<MockResultReferenceRequest>> validators)
        => new(validators);
    [Fact]
    public async Task Handle_Result_ShouldPassThrough_WhenNoValidators()
    {
        // Arrange
        var behavior = CreateResultBehavior([]);
        var request = new MockResultRequest();
        var expectedResponse = Result.Failure(Error.NullValue);
        var next = Substitute.For<RequestHandlerDelegate<Result>>();
        next.Invoke().Returns(Task.FromResult(expectedResponse));

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await next
            .Received(1)
            .Invoke();

        actualResponse
            .ShouldBeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task Handle_ResultPrimitive_ShouldPassThrough_WhenNoValidators()
    {
        // Arrange
        var behavior = CreateResultPrimitiveBehavior([]);
        var request = new MockResultPrimitiveRequest();
        var expectedResponse = Result.Failure<int>(Error.NullValue);
        var next = Substitute.For<RequestHandlerDelegate<Result<int>>>();
        next.Invoke().Returns(Task.FromResult(expectedResponse));

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await next
            .Received(1)
            .Invoke();

        actualResponse
            .ShouldBeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task Handle_ResultReference_ShouldPassThrough_WhenNoValidators()
    {
        // Arrange
        var behavior = CreateResultReferenceBehavior([]);
        var request = new MockResultReferenceRequest();
        var expectedResponse = Result.Failure<Response>(Error.NullValue);
        var next = Substitute.For<RequestHandlerDelegate<Result<Response>>>();
        next.Invoke().Returns(Task.FromResult(expectedResponse));

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await next
            .Received(1)
            .Invoke();

        actualResponse
            .ShouldBeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task Handle_Result_ShouldReturnFailure_WhenValidationFails()
    {
        // Arrange
        var validator = Substitute.For<IValidator<MockResultRequest>>();
        var failures = new List<ValidationFailure>
        {
            new("Property", "Error message")
        };
        var validationResult = new ValidationResult(failures);
        validator.ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult);

        var behavior = CreateResultBehavior([validator]);
        var request = new MockResultRequest();

        var next = Substitute.For<RequestHandlerDelegate<Result>>();

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        actualResponse.IsSuccess
            .ShouldBeFalse();

        actualResponse.Errors.Count.ShouldBe(1);

        var error = actualResponse.Errors[0];
        error.Code.ShouldBe("Property");
        error.Message.ShouldBe("Error message");
        error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_ResultPrimitive_ShouldReturnFailure_WhenValidationFails()
    {
        // Arrange
        var validator = Substitute.For<IValidator<MockResultPrimitiveRequest>>();
        var failures = new List<ValidationFailure>
        {
            new("Property", "Error message")
        };
        var validationResult = new ValidationResult(failures);
        validator.ValidateAsync(Arg.Any<MockResultPrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult);

        var behavior = CreateResultPrimitiveBehavior([validator]);
        var request = new MockResultPrimitiveRequest();

        var next = Substitute.For<RequestHandlerDelegate<Result<int>>>();

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        actualResponse.IsSuccess
            .ShouldBeFalse();

        actualResponse.Errors.Count.ShouldBe(1);

        var error = actualResponse.Errors[0];
        error.Code.ShouldBe("Property");
        error.Message.ShouldBe("Error message");
        error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_ResultReference_ShouldReturnFailure_WhenValidationFails()
    {
        // Arrange
        var validator = Substitute.For<IValidator<MockResultReferenceRequest>>();
        var failures = new List<ValidationFailure>
        {
            new("Property", "Error message")
        };
        var validationResult = new ValidationResult(failures);
        validator.ValidateAsync(Arg.Any<MockResultReferenceRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult);

        var behavior = CreateResultReferenceBehavior([validator]);
        var request = new MockResultReferenceRequest();

        var next = Substitute.For<RequestHandlerDelegate<Result<Response>>>();

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        actualResponse.IsSuccess
            .ShouldBeFalse();

        actualResponse.Errors.Count.ShouldBe(1);

        var error = actualResponse.Errors[0];
        error.Code.ShouldBe("Property");
        error.Message.ShouldBe("Error message");
        error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_Result_ShouldAggregateErrors_WhenMultipleValidatorsWithMultipleFailures()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator<MockResultRequest>>();
        var failures1 = new List<ValidationFailure>
        {
            new("Property1", "Error message1"),
            new("Property2", "Error message2")
        };
        var validationResult1 = new ValidationResult(failures1);
        validator1.ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult1);

        var validator2 = Substitute.For<IValidator<MockResultRequest>>();
        var failures2 = new List<ValidationFailure>
        {
            new("Property3", "Error message3"),
            new("Property4", "Error message4")
        };
        var validationResult2 = new ValidationResult(failures2);
        validator2.ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult2);

        var behavior = CreateResultBehavior([validator1, validator2]);
        var request = new MockResultRequest();

        var next = Substitute.For<RequestHandlerDelegate<Result>>();
        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        actualResponse.IsSuccess
            .ShouldBeFalse();

        actualResponse.Errors.Count
            .ShouldBe(4);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property1" && e.Message == "Error message1" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property2" && e.Message == "Error message2" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property3" && e.Message == "Error message3" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property4" && e.Message == "Error message4" && e.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_ResultPrimitive_ShouldAggregateErrors_WhenMultipleValidatorsWithMultipleFailures()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator<MockResultPrimitiveRequest>>();
        var failures1 = new List<ValidationFailure>
        {
            new("Property1", "Error message1"),
            new("Property2", "Error message2")
        };
        var validationResult1 = new ValidationResult(failures1);
        validator1.ValidateAsync(Arg.Any<MockResultPrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult1);

        var validator2 = Substitute.For<IValidator<MockResultPrimitiveRequest>>();
        var failures2 = new List<ValidationFailure>
        {
            new("Property3", "Error message3"),
            new("Property4", "Error message4")
        };
        var validationResult2 = new ValidationResult(failures2);
        validator2.ValidateAsync(Arg.Any<MockResultPrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult2);

        var behavior = CreateResultPrimitiveBehavior([validator1, validator2]);
        var request = new MockResultPrimitiveRequest();

        var next = Substitute.For<RequestHandlerDelegate<Result<int>>>();
        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        actualResponse.IsSuccess
            .ShouldBeFalse();

        actualResponse.Errors.Count
            .ShouldBe(4);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property1" && e.Message == "Error message1" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property2" && e.Message == "Error message2" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property3" && e.Message == "Error message3" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property4" && e.Message == "Error message4" && e.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_ResultReference_ShouldAggregateErrors_WhenMultipleValidatorsWithMultipleFailures()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator<MockResultReferenceRequest>>();
        var failures1 = new List<ValidationFailure>
        {
            new("Property1", "Error message1"),
            new("Property2", "Error message2")
        };
        var validationResult1 = new ValidationResult(failures1);
        validator1.ValidateAsync(Arg.Any<MockResultReferenceRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult1);

        var validator2 = Substitute.For<IValidator<MockResultReferenceRequest>>();
        var failures2 = new List<ValidationFailure>
        {
            new("Property3", "Error message3"),
            new("Property4", "Error message4")
        };
        var validationResult2 = new ValidationResult(failures2);
        validator2.ValidateAsync(Arg.Any<MockResultReferenceRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult2);

        var behavior = CreateResultReferenceBehavior([validator1, validator2]);
        var request = new MockResultReferenceRequest();

        var next = Substitute.For<RequestHandlerDelegate<Result<Response>>>();
        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        actualResponse.IsSuccess
            .ShouldBeFalse();

        actualResponse.Errors.Count
            .ShouldBe(4);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property1" && e.Message == "Error message1" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property2" && e.Message == "Error message2" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property3" && e.Message == "Error message3" && e.Type == ErrorType.Validation);

        actualResponse.Errors
            .ShouldContain(e => e.Code == "Property4" && e.Message == "Error message4" && e.Type == ErrorType.Validation);
    }


    [Fact]
    public async Task Handle_Result_ShouldPassThrough_WhenValidationSucceeds()
    {
        // Arrange
        var validator = Substitute.For<IValidator<MockResultRequest>>();
        var validationResult = new ValidationResult();
        validator.ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult);

        var behavior = CreateResultBehavior([validator]);
        var request = new MockResultRequest();
        var expectedResponse = Result.Success();
        var next = Substitute.For<RequestHandlerDelegate<Result>>();
        next.Invoke().Returns(Task.FromResult(expectedResponse));

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await next
            .Received(1)
            .Invoke();

        actualResponse
            .ShouldBeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task Handle_ResultPrimitive_ShouldPassThrough_WhenValidationSucceeds()
    {
        // Arrange
        var validator = Substitute.For<IValidator<MockResultPrimitiveRequest>>();
        var validationResult = new ValidationResult();
        validator.ValidateAsync(Arg.Any<MockResultPrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult);

        var behavior = CreateResultPrimitiveBehavior([validator]);
        var request = new MockResultPrimitiveRequest();
        var expectedResponse = Result.Success(10);
        var next = Substitute.For<RequestHandlerDelegate<Result<int>>>();
        next.Invoke().Returns(Task.FromResult(expectedResponse));

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await next
            .Received(1)
            .Invoke();

        actualResponse
            .ShouldBeEquivalentTo(expectedResponse);
    }
    [Fact]
    public async Task Handle_ResultReference_ShouldPassThrough_WhenValidationSucceeds()
    {
        // Arrange
        var validator = Substitute.For<IValidator<MockResultReferenceRequest>>();
        var validationResult = new ValidationResult();
        validator.ValidateAsync(Arg.Any<MockResultReferenceRequest>(), Arg.Any<CancellationToken>()).Returns(validationResult);

        var behavior = CreateResultReferenceBehavior([validator]);
        var request = new MockResultReferenceRequest();
        var expectedResponse = Result.Success(new Response("Test"));
        var next = Substitute.For<RequestHandlerDelegate<Result<Response>>>();
        next.Invoke().Returns(Task.FromResult(expectedResponse));

        // Act
        var actualResponse = await behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await next
            .Received(1)
            .Invoke();

        actualResponse
            .ShouldBeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task Handle_ShouldRunValidatorsOneAtATime_WhenThereAreSeveral()
    {
        // Arrange — the first validator hasn't finished: the second must not have started.
        var firstValidation = new TaskCompletionSource<ValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validator1 = Substitute.For<IValidator<MockResultRequest>>();
        validator1.ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>()).Returns(firstValidation.Task);
        var validator2 = Substitute.For<IValidator<MockResultRequest>>();
        validator2.ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>()).Returns(new ValidationResult());
        var behavior = CreateResultBehavior([validator1, validator2]);
        var next = Substitute.For<RequestHandlerDelegate<Result>>();
        next.Invoke(Arg.Any<CancellationToken>()).Returns(Result.Success());

        // Act
        var handling = behavior.Handle(new MockResultRequest(), next, CancellationToken.None);
        var secondStartedEarly = validator2.ReceivedCalls().Any();
        firstValidation.SetResult(new ValidationResult());
        var response = await handling;

        // Assert
        secondStartedEarly.ShouldBeFalse();
        await validator2.Received(1).ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>());
        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_ShouldPassTheCancellationTokenToTheValidators_WhenValidating()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var validator = Substitute.For<IValidator<MockResultRequest>>();
        validator.ValidateAsync(Arg.Any<MockResultRequest>(), Arg.Any<CancellationToken>()).Returns(new ValidationResult());
        var behavior = CreateResultBehavior([validator]);
        var next = Substitute.For<RequestHandlerDelegate<Result>>();
        next.Invoke(Arg.Any<CancellationToken>()).Returns(Result.Success());

        // Act
        await behavior.Handle(new MockResultRequest(), next, cancellation.Token);

        // Assert
        await validator.Received(1).ValidateAsync(Arg.Any<MockResultRequest>(), cancellation.Token);
    }

    [Fact]
    public async Task Handle_ShouldReturnATypedFailureEachTime_WhenTheSameResponseTypeFailsTwice()
    {
        // Arrange — the failure factory is built once per response type and reused.
        var validator = Substitute.For<IValidator<MockResultReferenceRequest>>();
        validator.ValidateAsync(Arg.Any<MockResultReferenceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Name", "Name is required.")]));
        var behavior = CreateResultReferenceBehavior([validator]);
        var next = Substitute.For<RequestHandlerDelegate<Result<Response>>>();

        // Act
        var first = await behavior.Handle(new MockResultReferenceRequest(), next, CancellationToken.None);
        var second = await behavior.Handle(new MockResultReferenceRequest(), next, CancellationToken.None);

        // Assert
        first.IsFailure.ShouldBeTrue();
        second.IsFailure.ShouldBeTrue();
        second.Errors.ShouldHaveSingleItem().Code.ShouldBe("Name");
        second.Errors[0].Type.ShouldBe(ErrorType.Validation);
        await next.DidNotReceive().Invoke(Arg.Any<CancellationToken>());
    }
}
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MockResultRequest : IRequest<Result> { }
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MockResultPrimitiveRequest : IRequest<Result<int>> { }
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MockResultReferenceRequest : IRequest<Result<Response>> { }
[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "NSubstitute (which uses Castle DynamicProxy) cannot generate a mock of a type containing inaccessible generic parameters")]
public sealed record Response(string Name);