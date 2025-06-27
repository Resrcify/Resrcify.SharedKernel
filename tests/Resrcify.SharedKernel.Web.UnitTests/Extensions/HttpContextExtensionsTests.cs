using Microsoft.AspNetCore.Http;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

public class HttpContextExtensionsTests
{
    [Fact]
    public void GetBearerToken_ShouldReturnToken_WhenHeaderStartsWithBearer()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer abc123token";

        // Act
        var token = context.GetBearerToken();

        // Assert
        token.ShouldBe("abc123token");
    }

    [Fact]
    public void GetBearerToken_ShouldReturnNull_WhenHeaderIsMissing()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var token = context.GetBearerToken();

        // Assert
        token.ShouldBeNull();
    }

    [Fact]
    public void GetBearerToken_ShouldReturnNull_WhenHeaderDoesNotStartWithBearer()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic xyz";

        // Act
        var token = context.GetBearerToken();

        // Assert
        token.ShouldBeNull();
    }
}