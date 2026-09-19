namespace ArquitecturaBase.Domain.Common;

/// <summary>
/// Entidad auditada. Los valores los completa el interceptor de Infrastructure al guardar
/// (fechas en UTC desde TimeProvider y usuario desde ICurrentUser); el dominio no los modifica.
/// Las propiedades deben ser públicas (no implementación explícita): el interceptor las busca por nombre.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; }

    Guid? CreatedBy { get; }

    DateTime? ModifiedAtUtc { get; }

    Guid? ModifiedBy { get; }
}
