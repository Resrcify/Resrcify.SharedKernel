using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

public class ResultExtensionsTests
{

    [Fact]
    public async Task Match_WithSuccessResultTask_ShouldReturnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success());

        // Act
        var actual = await resultTask.Match(
            onSuccess: () => Results.Ok(),
            onFailure: _ => Results.BadRequest());

        // Assert
        actual
            .ShouldBeOfType<Ok>();
    }

    [Fact]
    public async Task Match_WithFailureResultTask_ShouldReturnFailure()
    {
        // Arrange
        var error = new Error("Code", "Message", ErrorType.Validation);
        var resultTask = Task.FromResult(Result.Failure(error));

        // Act
        var actual = await resultTask.Match(
            onSuccess: () => Results.Ok(),
            onFailure: result => Results.BadRequest());

        // Assert
        actual
            .ShouldBeOfType<BadRequest>();
    }
    [Fact]
    public void ToProblemDetails_WithSuccessResult_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Success();

        // Act
        // Assert
        Should.Throw<InvalidOperationException>(result.ToProblemDetails);
    }

    [Theory]
    [InlineData(ErrorType.Validation, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1", "Bad Request", StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4", "Not Found", StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8", "Conflict", StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Failure, "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1", "Internal Server Error", StatusCodes.Status500InternalServerError)]
    public void ToProblemDetails_WithFailureResult_ShouldReturnProblemDetails(ErrorType errorType, string expectedType, string expectedTitle, int expectedStatusCode)
    {
        // Arrange
        var error = new Error("Code", "Message", errorType);
        var result = Result.Failure(error);

        // Act
        var problemDetails = result.ToProblemDetails() as ProblemHttpResult;

        // Assert
        problemDetails.ShouldNotBeNull();
        problemDetails?.ProblemDetails.Type.ShouldBe(expectedType);
        problemDetails?.ProblemDetails.Title.ShouldBe(expectedTitle);
        problemDetails?.ProblemDetails.Status.ShouldBe(expectedStatusCode);
        problemDetails?.StatusCode.ShouldBe(expectedStatusCode);
        problemDetails?.ProblemDetails.Extensions.ShouldContainKey("errors");
        problemDetails?.ProblemDetails.Extensions["errors"].ShouldBeAssignableTo<IEnumerable<Error>>();
        ((IEnumerable<Error>)problemDetails?.ProblemDetails.Extensions["errors"]!)
            .ShouldContain(error);
    }

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ConvertT_ShouldConvertToT_WhenHttpResponseMessageIsSuccess()
    {
        //Arrange
        string test = "Test";
        using var message = new HttpResponseMessage()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(test, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.Convert<string>();

        //Assert
        result.IsSuccess
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result<string>>();

        result.Value
            .ShouldBe(test);
    }

    [Fact]
    public async Task ConvertT_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsValidationError()
    {
        //Arrange
        string test = @"
        {
            ""type"": ""https://tools.ietf.org/html/rfc9110#section-15.5.1"",
            ""title"": ""One or more validation errors occurred."",
            ""status"": 400,
            ""errors"": {
                ""useCurrentGp"": [
                    ""The value 'null' is not valid.""
                ]
            },
            ""traceId"": ""00-4627c2f9dd83de593525c82b41f99e92-0f526beb524ba8c1-00""
        }";
        using var message = new HttpResponseMessage()
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent(test, Encoding.UTF8, "application/json")
        };
        var error = Error.Validation("useCurrentGp", "The value 'null' is not valid.");

        //Act
        var result = await message.Convert<string>();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result<string>>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors[0]
            .ShouldBe(error);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Failure)]
    public async Task ConvertT_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsFailure(HttpStatusCode httpStatusCode, ErrorType errorType)
    {
        //Arrange
        var error = new Error("Title", "Message", errorType);
        var resultObject = Result.Failure<string>(error);
        var problemDetails = resultObject.ToProblemDetails() as ProblemHttpResult;
        using var message = new HttpResponseMessage()
        {
            StatusCode = httpStatusCode,
            Content = new StringContent(JsonSerializer.Serialize(problemDetails!.ProblemDetails, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.Convert<string>();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result<string>>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors[0]
            .ShouldBe(error);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Failure)]
    public async Task ConvertT_ShouldReturnErrorArrayWithEmptyError_WhenHttpResponseMessageDoesntContainErrors(HttpStatusCode httpStatusCode, ErrorType errorType)
    {
        //Arrange
        var error = new Error("Title", "Message", errorType);
        var resultObject = Result.Failure<string>(error);
        var problemDetails = resultObject.ToProblemDetails() as ProblemHttpResult;

        problemDetails!.ProblemDetails.Extensions = new Dictionary<string, object?>();

        using var message = new HttpResponseMessage()
        {
            StatusCode = httpStatusCode,
            Content = new StringContent(JsonSerializer.Serialize(problemDetails!.ProblemDetails, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.Convert<string>();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result<string>>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors[0]
            .ShouldBe(Error.None);
    }

    [Fact]
    public async Task Convert_ShouldConvertToResult_WhenHttpResponseMessageIsSuccess()
    {
        //Arrange
        string test = "Test";
        using var message = new HttpResponseMessage()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(test, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.Convert();

        //Assert
        result.IsSuccess
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result>();
    }

    [Fact]
    public async Task Convert_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsValidationError()
    {
        //Arrange
        string test = @"
        {
            ""type"": ""https://tools.ietf.org/html/rfc9110#section-15.5.1"",
            ""title"": ""One or more validation errors occurred."",
            ""status"": 400,
            ""errors"": {
                ""useCurrentGp"": [
                    ""The value 'null' is not valid.""
                ]
            },
            ""traceId"": ""00-4627c2f9dd83de593525c82b41f99e92-0f526beb524ba8c1-00""
        }";
        using var message = new HttpResponseMessage()
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent(test, Encoding.UTF8, "application/json")
        };
        var error = Error.Validation("useCurrentGp", "The value 'null' is not valid.");

        //Act
        var result = await message.Convert();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors
            .ShouldContain(e => e.Code == error.Code && e.Message == error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Failure)]
    public async Task Convert_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsFailure(HttpStatusCode httpStatusCode, ErrorType errorType)
    {
        //Arrange
        var error = new Error("Title", "Message", errorType);
        var resultObject = Result.Failure<string>(error);
        var problemDetails = resultObject.ToProblemDetails() as ProblemHttpResult;
        using var message = new HttpResponseMessage()
        {
            StatusCode = httpStatusCode,
            Content = new StringContent(JsonSerializer.Serialize(problemDetails!.ProblemDetails, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.Convert();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors
            .ShouldContain(e => e.Code == error.Code && e.Message == error.Message && e.Type == error.Type);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Failure)]
    public async Task Convert_ShouldReturnErrorArrayWithEmptyError_WhenHttpResponseMessageDoesntContainErrors(HttpStatusCode httpStatusCode, ErrorType errorType)
    {
        //Arrange
        var error = new Error("Title", "Message", errorType);
        var resultObject = Result.Failure<string>(error);
        var problemDetails = resultObject.ToProblemDetails() as ProblemHttpResult;

        problemDetails!.ProblemDetails.Extensions = new Dictionary<string, object?>();

        using var message = new HttpResponseMessage()
        {
            StatusCode = httpStatusCode,
            Content = new StringContent(JsonSerializer.Serialize(problemDetails!.ProblemDetails, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.Convert();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors[0]
            .ShouldBe(Error.None);
    }
}