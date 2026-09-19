using ArquitecturaBase.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>Usuario de Identity con el perfil de la sección 4.3. El UserName es el email.</summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable
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
}
