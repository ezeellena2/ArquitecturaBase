namespace ArquitecturaBase.Domain.Common;

/// <summary>Raíz de agregado: acumula los eventos de dominio que produce.</summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }

    // Métodos y no propiedades: así EF Core no intenta mapear los eventos.
    public IReadOnlyCollection<IDomainEvent> GetDomainEvents() => _domainEvents.ToArray();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
