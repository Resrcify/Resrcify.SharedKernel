using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests;

/// <summary>
/// The Web assembly in a service that references none of its optional (private) packages: JwtBearer for
/// AddResrcifyJwtBearer, Http.Resilience and Polly for AddResultResilience. MVC loads every type of it to find
/// controllers (it has ApiController), so no type may need those packages just to load: no base type, interface,
/// generic argument of either, or value-type field (static too) from them. Methods may use them; they load only when
/// called.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class WebAssemblyTests
{
    private static readonly HashSet<string> OptionalPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.AspNetCore.Authentication.JwtBearer",
        "Microsoft.Extensions.Http.Resilience",
        "Microsoft.Extensions.Resilience",
        "Polly.Core",
        "Polly.Extensions",
        "Polly.RateLimiting",
    };

    [Fact]
    public void DefinedTypes_ShouldLoadWithoutTheOptionalPackages_WhenMvcScansTheAssembly()
    {
        var web = typeof(HttpResultExtensions).Assembly;

        var needingAnOptionalPackage = web.DefinedTypes
            .Where(type => NeededToLoad(type).Any(IsFromAnOptionalPackage))
            .Select(type => type.FullName)
            .ToList();

        needingAnOptionalPackage.ShouldBeEmpty();
    }

    private static IEnumerable<Type> NeededToLoad(TypeInfo type)
    {
        if (type.BaseType is not null)
            yield return type.BaseType;
        foreach (var implemented in type.ImplementedInterfaces)
            yield return implemented;
        foreach (var field in type.DeclaredFields.Where(field => field.FieldType.IsValueType))
            yield return field.FieldType;
    }

    private static bool IsFromAnOptionalPackage(Type type)
        => OptionalPackages.Contains(type.Assembly.GetName().Name ?? string.Empty)
            || type.GenericTypeArguments.Any(IsFromAnOptionalPackage)
            || type.HasElementType && IsFromAnOptionalPackage(type.GetElementType()!);
}
