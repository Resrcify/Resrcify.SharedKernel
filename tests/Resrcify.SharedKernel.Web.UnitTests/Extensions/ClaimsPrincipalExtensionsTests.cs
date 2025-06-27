using System;
using System.Security.Claims;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetUserId_ShouldReturnUserId_WhenClaimExistsAndIsValidGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, guid.ToString())
        ]));

        // Act
        var result = claimsPrincipal.GetUserId();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(guid);
    }

    [Fact]
    public void GetUserId_ShouldReturnFailure_WhenClaimIsMissing()
    {
        // Arrange
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = claimsPrincipal.GetUserId();

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldContain(ClaimsPrincipalExtensions.MissingUserIdClaim);
    }

    [Fact]
    public void GetUserId_ShouldReturnFailure_WhenClaimIsNotAGuid()
    {
        // Arrange
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "not-a-guid")
        ]));

        // Act
        var result = claimsPrincipal.GetUserId();

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldContain(ClaimsPrincipalExtensions.MissingUserIdClaim);
    }
}