using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>Entidad de prueba: existe solo en este proyecto.</summary>
public sealed class Widget : AggregateRoot, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 50;

    public Widget(string name)
    {
        Name = name;
    }

    // Para EF Core.
    private Widget()
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public void Rename(string name) => Name = name;
}
