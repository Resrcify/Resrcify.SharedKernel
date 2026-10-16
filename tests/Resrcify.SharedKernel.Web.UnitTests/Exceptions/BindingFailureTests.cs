using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Exceptions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Exceptions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class BindingFailureTests
{
    // ASP.NET Core's messages (RequestDelegateFactory), and what is answered for them.
    public static TheoryData<string, string, string> Messages => new()
    {
        {
            "Required parameter \"int page\" was not provided from query string.",
            "page",
            "The required parameter \"page\" was not provided from query string."
        },
        {
            "Required parameter \"string X-Tenant\" was not provided from header.",
            "X-Tenant",
            "The required parameter \"X-Tenant\" was not provided from header."
        },
        {
            "Failed to bind parameter \"Guid id\" from \"secret-value\".",
            "id",
            "The value of parameter \"id\" is not valid."
        },
        {
            "Failed to bind parameter \"Dictionary<string, int> filters\" from \"x\".",
            "filters",
            "The value of parameter \"filters\" is not valid."
        },
        {
            "Failed to read parameter \"CreateShardRequest request\" from the request body as JSON.",
            "request",
            "The parameter \"request\" couldn't be read from the request body."
        },
        {
            "Implicit body inferred for parameter \"request\" but no body was provided. Did you mean to use a Service instead?",
            "request",
            "The request body for parameter \"request\" was not provided."
        },
        {
            "Invalid anti-forgery token found when reading parameter \"IFormFile file\" from the request body as form.",
            "file",
            "The parameter \"file\" couldn't be bound from the request."
        },
        {
            "Unexpected end of request content.",
            ErrorCodes.Request.Binding,
            "The request couldn't be read."
        },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void Describe_ShouldNameTheParameter_AndNeitherItsTypeNorTheValueSent(
        string message,
        string code,
        string answered)
    {
        var (error, statusCode) = BindingFailure.Describe(new BadHttpRequestException(message));

        statusCode.ShouldBe(StatusCodes.Status400BadRequest);
        error.ShouldBe(Error.Validation(code, answered));
        error.Message.ShouldNotContain("secret-value");
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge, StatusCodes.Status413PayloadTooLarge, "The request is too large.")]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, StatusCodes.Status415UnsupportedMediaType, "The request's content type isn't supported.")]
    [InlineData(StatusCodes.Status431RequestHeaderFieldsTooLarge, StatusCodes.Status431RequestHeaderFieldsTooLarge, "The request's headers are too large.")]
    [InlineData(StatusCodes.Status500InternalServerError, StatusCodes.Status400BadRequest, "The request couldn't be read.")]
    public void Describe_ShouldKeepTheExceptionsClientErrorStatus(
        int thrown,
        int answered,
        string message)
    {
        var (error, statusCode) = BindingFailure.Describe(new BadHttpRequestException("Something about the request.", thrown));

        statusCode.ShouldBe(answered);
        error.ShouldBe(Error.Validation(ErrorCodes.Request.Binding, message));
    }
}
