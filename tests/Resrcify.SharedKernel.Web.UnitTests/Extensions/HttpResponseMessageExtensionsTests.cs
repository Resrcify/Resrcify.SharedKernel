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

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class HttpResponseMessageExtensionsTests
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ToResultAsyncT_ShouldConvertToT_WhenHttpResponseMessageIsSuccess()
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
        var result = await message.ToResultAsync<string>();

        //Assert
        result.IsSuccess
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result<string>>();

        result.Value
            .ShouldBe(test);
    }

    [Fact]
    public async Task ToResultAsyncT_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsValidationError()
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
        var result = await message.ToResultAsync<string>();

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
    public async Task ToResultAsyncT_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsFailure(
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
        var result = await message.ToResultAsync<string>();

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
    public async Task ToResultAsyncT_ShouldReturnAnErrorForTheStatus_WhenHttpResponseMessageDoesntContainErrors(HttpStatusCode httpStatusCode, ErrorType errorType)
    {
        //Arrange
        var error = new Error("Title", "Message", errorType);
        var resultObject = Result.Failure<string>(error);
        var problemDetails = resultObject.ToProblemDetails() as ProblemHttpResult;

        problemDetails!.ProblemDetails.Extensions = new Dictionary<string, object?>();

        using var message = new HttpResponseMessage()
        {
            StatusCode = httpStatusCode,
            Content = new StringContent(JsonSerializer.Serialize(problemDetails.ProblemDetails, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.ToResultAsync<string>();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result<string>>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors[0].Code
            .ShouldBe($"Http.{(int)httpStatusCode}");

        result.Errors[0].Type
            .ShouldBe(errorType);
    }

    [Fact]
    public async Task ToResultAsync_ShouldConvertToResult_WhenHttpResponseMessageIsSuccess()
    {
        //Arrange
        string test = "Test";
        using var message = new HttpResponseMessage()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(test, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.ToResultAsync();

        //Assert
        result.IsSuccess
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result>();
    }

    [Fact]
    public async Task ToResultAsync_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsValidationError()
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
        var result = await message.ToResultAsync();

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
    public async Task ToResultAsync_ShouldConvertToProblemsDetails_WhenHttpResponseMessageIsFailure(HttpStatusCode httpStatusCode, ErrorType errorType)
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
        var result = await message.ToResultAsync();

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
    public async Task ToResultAsync_ShouldReturnAnErrorForTheStatus_WhenHttpResponseMessageDoesntContainErrors(HttpStatusCode httpStatusCode, ErrorType errorType)
    {
        //Arrange
        var error = new Error("Title", "Message", errorType);
        var resultObject = Result.Failure<string>(error);
        var problemDetails = resultObject.ToProblemDetails() as ProblemHttpResult;

        problemDetails!.ProblemDetails.Extensions = new Dictionary<string, object?>();

        using var message = new HttpResponseMessage()
        {
            StatusCode = httpStatusCode,
            Content = new StringContent(JsonSerializer.Serialize(problemDetails.ProblemDetails, _options), Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.ToResultAsync();

        //Assert
        result.IsFailure
            .ShouldBeTrue();

        result
            .ShouldBeOfType<Result>();

        result.Errors
            .ShouldHaveSingleItem();

        result.Errors[0].Code
            .ShouldBe($"Http.{(int)httpStatusCode}");

        result.Errors[0].Type
            .ShouldBe(errorType);
    }

    [Theory]
    [InlineData("<html><body>502 Bad Gateway</body></html>", "text/html")]
    [InlineData("", "application/json")]
    [InlineData("null", "application/json")]
    [InlineData("{\"errors\": [\"not an error object\"]}", "application/problem+json")]
    public async Task ToResultAsync_ShouldReturnAnErrorForTheStatus_WhenTheErrorBodyIsNotProblemDetails(string body, string mediaType)
    {
        //Arrange — a response's content can be read once, so each conversion gets its own
        using var message = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };
        using var typedMessage = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };

        //Act
        var result = await message.ToResultAsync();
        var typed = await typedMessage.ToResultAsync<string>();

        //Assert
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("Http.502");
        result.Errors[0].Type.ShouldBe(ErrorType.ExternalFailure);
        typed.Errors.ShouldHaveSingleItem().Code.ShouldBe("Http.502");
    }

    [Fact]
    public async Task ToResultAsyncT_ShouldReturnAnEmptyContentFailure_WhenTheResponseIsNoContent()
    {
        //Arrange
        using var message = new HttpResponseMessage(HttpStatusCode.NoContent);

        //Act
        var result = await message.ToResultAsync<string>();

        //Assert
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("Http.EmptyContent");
        result.Errors[0].Type.ShouldBe(ErrorType.ExternalFailure);
    }

    [Fact]
    public async Task ToResultAsyncT_ShouldReturnAnEmptyContentFailure_WhenTheBodyIsJsonNull()
    {
        //Arrange
        using var message = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };

        //Act
        var result = await message.ToResultAsync<string>();

        //Assert
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("Http.EmptyContent");
    }

    [Fact]
    public async Task ToResultAsyncT_ShouldReturnAnUnreadableContentFailure_WhenTheSuccessBodyIsNotJson()
    {
        //Arrange
        using var message = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>maintenance</html>", Encoding.UTF8, "text/html")
        };

        //Act
        var result = await message.ToResultAsync<string>();

        //Assert
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("Http.UnreadableContent");
        result.Errors[0].Type.ShouldBe(ErrorType.ExternalFailure);
    }
}
