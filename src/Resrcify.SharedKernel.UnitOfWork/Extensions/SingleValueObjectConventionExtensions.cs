using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Converters;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// Maps every property of a single-value value object (<see cref="ISingleValueObject{TSelf, TValue}"/>) as its value,
/// so a service no longer writes <c>HasConversion(x =&gt; x.Value, v =&gt; X.Create(v).Value)</c> per property.
/// </summary>
/// <remarks>
/// <para>
/// Each opted-in type gets <see cref="SingleValueObjectConverter{TSelf, TValue}"/> as pre-convention configuration
/// (<c>ConfigureConventions</c>), on every property of the type: nullable ones, keys, foreign keys, indexed ones, and
/// those of owned and complex types. The column is what the hand-written conversion gave: the value's type, the
/// property's nullability, and whatever else the property's configuration says (<c>HasMaxLength</c> stays where it
/// is). Reads call <see cref="ISingleValueObject{TSelf, TValue}.FromPersisted"/>, by default <c>Create(value).Value</c>,
/// so a stored value that no longer validates throws while it is read, as before.
/// </para>
/// <para>
/// Types that don't implement the interface are left alone, and a property's own <c>HasConversion</c> (e.g. one that
/// stores <see langword="null"/> as <c>0</c>) still wins over the convention. Elements of a primitive collection are
/// not converted by it: keep their <c>ElementType(e =&gt; e.HasConversion(...))</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class PlayerId
///     : ValueObject,
///     ISingleValueObject&lt;PlayerId, string&gt;
/// { ... }   // its existing Value and Create(string) are all the interface asks for
///
/// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
///     => configurationBuilder.AddSingleValueObjectConversions(typeof(PlayerId).Assembly);
/// </code>
/// </example>
public static class SingleValueObjectConventionExtensions
{
    /// <summary>
    /// Converts every property whose type, declared in <paramref name="assemblies"/>, implements
    /// <see cref="ISingleValueObject{TSelf, TValue}"/> for itself. Call it from <c>ConfigureConventions</c>.
    /// </summary>
    /// <exception cref="ArgumentException">No assembly is given.</exception>
    /// <exception cref="InvalidOperationException">A type implements the interface for more than one value type.</exception>
    public static ModelConfigurationBuilder AddSingleValueObjectConversions(
        this ModelConfigurationBuilder configurationBuilder,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException(
                "Name at least one assembly to look for single-value value objects in.",
                nameof(assemblies));

        return configurationBuilder.AddSingleValueObjectConversions(
            assemblies
                .Distinct()
                .SelectMany(assembly => assembly.GetTypes()));
    }

    /// <summary>The convention for the opted-in types among <paramref name="candidates"/>.</summary>
    internal static ModelConfigurationBuilder AddSingleValueObjectConversions(
        this ModelConfigurationBuilder configurationBuilder,
        IEnumerable<Type> candidates)
    {
        foreach (var (valueObjectType, valueType) in FindSingleValueObjects(candidates))
            configurationBuilder
                .Properties(valueObjectType)
                .HaveConversion(
                    typeof(SingleValueObjectConverter<,>).MakeGenericType(
                        valueObjectType,
                        valueType));

        return configurationBuilder;
    }

    private static IEnumerable<(Type ValueObjectType, Type ValueType)> FindSingleValueObjects(
        IEnumerable<Type> candidates)
    {
        foreach (var type in candidates.Where(IsConcreteClass))
        {
            var valueTypes = SingleValueTypesOf(type);
            if (valueTypes.Count > 1)
                throw new InvalidOperationException(
                    $"{type.Name} implements ISingleValueObject<{type.Name}, TValue> for more than one value type " +
                    $"({string.Join(", ", valueTypes.Select(valueType => valueType.Name))}): it can be stored as one only.");
            if (valueTypes.Count == 1)
                yield return (type, valueTypes[0]);
        }
    }

    private static bool IsConcreteClass(Type type)
        => type.IsClass &&
            !type.IsAbstract &&
            !type.ContainsGenericParameters;

    private static List<Type> SingleValueTypesOf(Type type)
        => type
            .GetInterfaces()
            .Where(contract =>
                contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(ISingleValueObject<,>) &&
                contract.GenericTypeArguments[0] == type)
            .Select(contract => contract.GenericTypeArguments[1])
            .ToList();
}
