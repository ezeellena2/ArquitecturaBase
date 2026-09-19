namespace ArquitecturaBase.Domain.Common;

/// <summary>Entidad con identidad. El Id es un Guid v7, ordenable por momento de creación.</summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity()
        : this(Guid.CreateVersion7())
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The id cannot be empty.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; private init; }

    public bool Equals(Entity? other) => other is not null && other.GetType() == GetType() && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
