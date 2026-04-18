using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;
using ResultExtensions = Resrcify.SharedKernel.Web.Extensions.ResultExtensions;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class ResultExtensionsTests
{
    [Fact]
    public async Task Match_WithSuccessResultTask_ShouldReturnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success());

        // Act
        var actual = await resultTask.Match(
            onSuccess: () => Microsoft.AspNetCore.Http.Results.Ok(),
            onFailure: _ => Microsoft.AspNetCore.Http.Results.BadRequest());

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
            onSuccess: () => Microsoft.AspNetCore.Http.Results.Ok(),
            onFailure: result => Microsoft.AspNetCore.Http.Results.BadRequest());

        // Assert
        actual
            .ShouldBeOfType<BadRequest>();
    }


    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest, "Bad Request", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1")]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized, "Unauthorized", "https://datatracker.ietf.org/doc/html/rfc7235#section-3.1")]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden, "Forbidden", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3")]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound, "Not Found", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4")]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict, "Conflict", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8")]
    [InlineData(ErrorType.Timeout, StatusCodes.Status504GatewayTimeout, "Request Timeout", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.5")]
    [InlineData(ErrorType.RateLimit, StatusCodes.Status429TooManyRequests, "Too Many Requests", "https://datatracker.ietf.org/doc/html/rfc6585#section-4")]
    [InlineData(ErrorType.ExternalFailure, StatusCodes.Status502BadGateway, "Bad Gateway", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.3")]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError, "Internal Server Error", "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1")]
    public void ToProblemDetails_WithFailureResult_ShouldReturnProblemDetails(
        ErrorType errorType,
        int expectedStatusCode,
        string expectedTitle,
        string expectedType)
    {
        // Arrange
        var error = new Error("TestCode", "Test message", errorType);
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
        ((IEnumerable<Error>)problemDetails?.ProblemDetails.Extensions["errors"]!).ShouldContain(error);
    }

    [Theory]
    [MemberData(nameof(AllErrorCombinations))]
    public void ToProblemDetails_ShouldUseDominantErrorType_WhenMultipleErrors(ErrorType[] inputTypes)
    {
        // Arrange
        var errors = inputTypes
            .Select((t, i) => new Error($"E00{i + 1}", $"Message {i + 1}", t))
            .ToArray();

        var result = Result.Failure(errors);

        // Act
        var httpResult = result.ToProblemDetails() as ProblemHttpResult;

        // Assert
        httpResult.ShouldNotBeNull();

        var problem = httpResult!.ProblemDetails;
        problem.ShouldNotBeNull();

        var dominant = typeof(ResultExtensions)
            .GetMethod("GetDominantErrorType", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [errors]) as ErrorType? ?? ErrorType.Failure;

        var expectedStatus = ResultExtensions.GetStatusCode(dominant);
        var expectedTitle = ResultExtensions.GetTitle(dominant);
        var expectedType = ResultExtensions.GetType(dominant);

        problem.Status.ShouldBe(expectedStatus);
        problem.Title.ShouldBe(expectedTitle);
        problem.Type.ShouldBe(expectedType);

        problem.Extensions.ShouldContainKey("errors");
        var extensionErrors = problem.Extensions["errors"];
        extensionErrors.ShouldBeAssignableTo<IEnumerable<Error>>();

        var errorList = ((IEnumerable<Error>)extensionErrors!).ToArray();
        errorList.ShouldBe(errors);
    }
    public static IEnumerable<object[]> AllErrorCombinations()
    {
        var types = Enum.GetValues<ErrorType>();

        foreach (var first in types)
        {
            foreach (var second in types)
            {
                if (first != second)
                {
                    yield return new object[] { new[] { first, second } };
                }
            }
        }
    }

    [Fact]
    public void ToProblemDetails_WithSuccessResult_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var result = Result.Success();

        // Act & Assert
        var act = result.ToProblemDetails;

        // Verify that an exception is thrown
        var exception = act.ShouldThrow<InvalidOperationException>();
        exception.Message.ShouldBe("Successful result should not be converted to problem details.");
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
            Content = new StringContent(
                JsonSerializer.Serialize(
                    test,
                    _options),
                Encoding.UTF8,
                "application/json")
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
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.ExternalFailure)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
    [InlineData((HttpStatusCode)429, ErrorType.RateLimit)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Failure)]
    public async Task ConvertT_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsFailure(
        HttpStatusCode httpStatusCode, ErrorType errorType)
    {
        //Arrange
        var error = new Error("Title", "Message", errorType);
        var resultObject = Result.Failure<string>(error);
        var problemDetails = resultObject.ToProblemDetails() as ProblemHttpResult;
        using var message = new HttpResponseMessage()
        {
            StatusCode = httpStatusCode,
            Content = new StringContent(
                JsonSerializer.Serialize(problemDetails!.ProblemDetails, _options), Encoding.UTF8, "application/json")
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
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.ExternalFailure)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
    [InlineData((HttpStatusCode)429, ErrorType.RateLimit)]
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
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.ExternalFailure)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
    [InlineData((HttpStatusCode)429, ErrorType.RateLimit)]
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
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.ExternalFailure)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
    [InlineData((HttpStatusCode)429, ErrorType.RateLimit)]
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