using System;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; private init; }

    protected Entity(TId id)
    {
        Id = id;
    }

    public static bool operator ==(Entity<TId> first, Entity<TId> second)
        => first is not null &&
            second is not null &&
            first.Equals(second);

    public static bool operator !=(Entity<TId> first, Entity<TId> second)
        => !(first == second);

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;
        if (obj.GetType() != GetType())
            return false;
        if (obj is not Entity<TId> entity)
            return false;
        return Id.Equals(entity.Id);
    }

    public override string ToString()
        => Id?.ToString() ?? string.Empty;

    public override int GetHashCode()
        => Id.GetHashCode() * 41;

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
            return false;
        if (other.GetType() != GetType())
            return false;
        return Id.Equals(other.Id);
    }
}