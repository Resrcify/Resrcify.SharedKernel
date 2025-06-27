using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

public abstract class Enumeration<TEnum> : IEquatable<Enumeration<TEnum>>
    where TEnum : Enumeration<TEnum>
{
    public static readonly Dictionary<int, TEnum> Enumerations = CreateEnumeration();

    public int Value { get; protected init; }
    public string Name { get; protected init; }
    protected Enumeration(int value, string name)
    {
        Value = value;
        Name = name;
    }
    public static TEnum? FromValue(int value)
        => Enumerations.TryGetValue(
            value,
            out TEnum? enumeration) ?
                enumeration :
                null;

    public static TEnum? FromName(string name)
        => Enumerations.Values
            .SingleOrDefault(e => e.Name == name);

    public bool Equals(Enumeration<TEnum>? other)
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

    private static Dictionary<int, TEnum> CreateEnumeration()
    {
        var enumerationType = typeof(TEnum);

        var fieldForType = enumerationType
            .GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.FlattenHierarchy)
            .Where(fieldInfo =>
                enumerationType.IsAssignableFrom(fieldInfo.FieldType))
            .Select(fieldInfo =>
                (TEnum)fieldInfo.GetValue(default)!);

        return fieldForType.ToDictionary(x => x.Value);
    }
}
