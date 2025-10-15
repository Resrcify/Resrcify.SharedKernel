using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

public abstract class Enumeration<TEnum>
    : IEquatable<Enumeration<TEnum>>
    where TEnum : Enumeration<TEnum>
{
    private static readonly Dictionary<int, TEnum> _enumerations = CreateEnumeration();
    public static IReadOnlyDictionary<int, TEnum> Enumerations
        => _enumerations;

    public int Value { get; protected init; }
    public string Name { get; protected init; }
    protected Enumeration(
        int value,
        string name)
    {
        Value = value;
        Name = name;
    }
    public static TEnum? FromValue(
        int value)
        => Enumerations.TryGetValue(
            value,
            out TEnum? enumeration) ?
                enumeration :
                null;

    public static TEnum? FromName(
        string name)
        => Enumerations.Values
            .SingleOrDefault(e =>
                string.Equals(
                    e.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));

    public static implicit operator int(
        Enumeration<TEnum> e)
        => e.Value;
    public static implicit operator string(
        Enumeration<TEnum> e)
        => e.Name;
    public static bool TryFromValue(
        int value,
        out TEnum? result)
        => Enumerations.TryGetValue(
            value,
            out result);

    public static bool TryFromName(
        string name,
        out TEnum? result)
    {
        result = Enumerations.Values
            .SingleOrDefault(e =>
                string.Equals(
                    e.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
        return result is not null;
    }
    public bool Equals(
        Enumeration<TEnum>? other)
    {
        if (other is null)
            return false;

        return
            GetType() == other.GetType() &&
            Value == other.Value;
    }
    public override bool Equals(object? obj)
        => obj is Enumeration<TEnum> other &&
            Equals(other);

    public override int GetHashCode()
        => Value.GetHashCode();

    public override string ToString()
        => Name;
    protected static void Register(
        TEnum instance)
    {
        _enumerations.TryAdd(
            instance.Value,
            instance);
    }
    private static Dictionary<int, TEnum> CreateEnumeration()
    {
        var type = typeof(TEnum);

        var fields = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a =>
            {
                try
                {
                    return !a.IsDynamic &&
                        a.GetTypes()
                            .Any(t => type.IsAssignableFrom(t));
                }
                catch
                {
                    return false;
                }
            })
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes()
                        .Where(t => type.IsAssignableFrom(t))
                        .SelectMany(t => t.GetFields(
                            BindingFlags.Public |
                            BindingFlags.Static |
                            BindingFlags.FlattenHierarchy))
                        .Where(f => type.IsAssignableFrom(f.FieldType))
                        .Select(f => (TEnum)f.GetValue(null)!);
                }
                catch
                {
                    return [];
                }
            })
            .DistinctBy(e => e.Value)
            .ToDictionary(e => e.Value);

        return fields;
    }
}
