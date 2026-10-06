using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

/// <summary>
/// A smart enum: its members are the <c>public static</c> fields of type <typeparamref name="TEnum"/> declared on
/// <typeparamref name="TEnum"/> or on its subclasses in the same assembly.
/// </summary>
/// <remarks>
/// The members are collected once, on first use. Two members with the same value, or with names that differ only in
/// case, are a mistake: the first lookup throws, naming them. A member that refers to another during the enum's own
/// initialization (e.g. <c>Default = FromName("Red")</c>) sees the members initialized so far, and the full set is
/// collected once initialization has finished.
/// </remarks>
public abstract class Enumeration<TEnum>
    : IEquatable<Enumeration<TEnum>>
    where TEnum : Enumeration<TEnum>
{
    private static MemberSet<TEnum>? _members;

    public static IReadOnlyDictionary<int, TEnum> Enumerations
        => GetMembers().ByValue;

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
        => GetMembers().ByValue.GetValueOrDefault(value);

    public static TEnum? FromName(
        string name)
        => GetMembers().ByName.GetValueOrDefault(name);

    public static implicit operator int(
        Enumeration<TEnum> e)
        => e.Value;
    public static implicit operator string(
        Enumeration<TEnum> e)
        => e.Name;
    public static bool TryFromValue(
        int value,
        out TEnum? result)
    {
        result = FromValue(value);
        return result is not null;
    }

    public static bool TryFromName(
        string name,
        out TEnum? result)
    {
        result = FromName(name);
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

    private static MemberSet<TEnum> GetMembers()
    {
        if (Volatile.Read(ref _members) is { } members)
            return members;
        // Two threads collecting at once both get the same members, so no lock is needed. Fields still null mean the
        // enum is initializing (a member refers to another): answer from what is there, and collect again next time.
        var (collected, complete) = Collect();
        if (complete)
            Volatile.Write(ref _members, collected);
        return collected;
    }

    private static (MemberSet<TEnum> Members, bool Complete) Collect()
    {
        var type = typeof(TEnum);
        var fields = type.Assembly
            .GetTypes()
            .Where(type.IsAssignableFrom)
            .SelectMany(declaring => declaring.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(field => type.IsAssignableFrom(field.FieldType))
            .Select(field => field.GetValue(null) as TEnum)
            .ToList();
        var values = fields.OfType<TEnum>().Distinct<TEnum>(ReferenceEqualityComparer.Instance).ToList();

        ThrowOnDuplicates(values.GroupBy(member => member.Value).Where(group => group.Count() > 1), "value");
        ThrowOnDuplicates(values.GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1), "name");

        var members = new MemberSet<TEnum>(
            values.ToFrozenDictionary(member => member.Value),
            values.ToFrozenDictionary(member => member.Name, StringComparer.OrdinalIgnoreCase));
        return (members, !fields.Contains(null));
    }

    private static void ThrowOnDuplicates<TKey>(IEnumerable<IGrouping<TKey, TEnum>> duplicates, string what)
    {
        var first = duplicates.FirstOrDefault();
        if (first is not null)
            throw new InvalidOperationException(
                $"{typeof(TEnum).Name} has more than one member with the {what} '{first.Key}': " +
                string.Join(", ", first.Select(member => member.Name)) + ".");
    }

    private sealed record MemberSet<TMember>(
        FrozenDictionary<int, TMember> ByValue,
        FrozenDictionary<string, TMember> ByName);
}
