using System;
using System.Security.Claims;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Result<Guid> GetUserId(
        this ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (Guid.TryParse(
            userIdClaim,
            out Guid userId))
            return userId;

        return Result.Failure<Guid>(
            MissingUserIdClaim);
    }

    // The caller isn't identified: a 401, not a problem with the request.
    internal static readonly Error MissingUserIdClaim = Error.Unauthorized(
        "User.MissingUserIdClaim",
        $"User Id claim not found or invalid.");
}