namespace OrderToCash.SharedKernel;

/// <summary>
/// Base type for anything with identity rather than value — equality is by
/// <see cref="Id"/> (and runtime type), never by the values of any other
/// field (CLAUDE.md, coding conventions: "Entity/AggregateRoot are classes
/// with identity equality").
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity(UniqueId id) => Id = id;

    public UniqueId Id { get; }

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);

    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && Id.Equals(other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
