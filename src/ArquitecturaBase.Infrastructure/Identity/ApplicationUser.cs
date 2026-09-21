using ArquitecturaBase.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Usuario de Identity con el perfil de la sección 4.3. El UserName es el email. Se borra lógicamente: el filtro
/// global lo saca de todas las consultas, pero su historial de ingresos sigue existiendo (sección 7 del spec de la
/// Fase 4). El índice único del email sigue cubriendo las filas borradas, así que dar de alta ese correo de nuevo
/// restaura la cuenta en lugar de insertar otra.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable, ISoftDeletable
{
    public const int DisplayNameMaxLength = 100;
    public const int CultureMaxLength = 10;
    public const int TimeZoneIdMaxLength = 64;
    public const string DefaultCulture = "es";
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string? DisplayName { get; set; }

    public string Culture { get; set; } = DefaultCulture;

    /// <summary>Zona horaria IANA con la que el front muestra las fechas.</summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public Guid? DeletedBy { get; private set; }

    /// <summary>Deshace el borrado lógico. Lo usa el alta de usuarios cuando el correo ya tuvo una cuenta.</summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAtUtc = null;
        DeletedBy = null;
    }
}
