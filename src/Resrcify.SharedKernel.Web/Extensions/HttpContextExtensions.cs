using System;
using Microsoft.AspNetCore.Http;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class HttpContextExtensions
{
    public static string? GetBearerToken(this HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";

        return header.AsSpan().StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? header.AsSpan(bearerPrefix.Length).ToString()
            : null;
    }
}