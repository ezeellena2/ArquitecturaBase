namespace ArquitecturaBase.Domain.Common;

/// <summary>
/// Entidad que se marca como borrada en lugar de eliminarse. Un filtro global de EF Core
/// la excluye de las consultas. Las propiedades deben ser públicas (no implementación explícita).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTime? DeletedAtUtc { get; }

    Guid? DeletedBy { get; }
}
