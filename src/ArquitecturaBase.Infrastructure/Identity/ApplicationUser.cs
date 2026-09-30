using System.Globalization;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Usuario de Identity con el perfil de la sección 4.3. El UserName es el Id: el correo y el número de WhatsApp
/// (PhoneNumber, en formato internacional) son opcionales, aunque toda cuenta tiene al menos uno, y cambiar uno no
/// tiene que cambiar nada más (sección 6.1 del spec del ingreso con WhatsApp). Se borra lógicamente: el filtro
/// global lo saca de todas las consultas, pero su historial de ingresos sigue existiendo (sección 7 del spec de la
/// Fase 4). Los índices únicos del email y del número siguen cubriendo las filas borradas, así que dar de alta ese
/// correo de nuevo restaura la cuenta en lugar de insertar otra. El nombre, el idioma, la zona horaria y el estado se
/// cambian con sus métodos, que aplican las reglas de <see cref="AccountRules"/>; EF los lee por el campo. El
/// constructor sin parámetros queda para Identity, EF y los tests que arman usuarios con un inicializador de objeto.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable, ISoftDeletable
{
    public const int DisplayNameMaxLength = AccountRules.DisplayNameMaxLength;
    public const int CultureMaxLength = AccountRules.CultureMaxLength;
    public const int TimeZoneIdMaxLength = AccountRules.TimeZoneIdMaxLength;
    public const string DefaultCulture = "es";
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string? DisplayName { get; private set; }

    public string Culture { get; private set; } = DefaultCulture;

    /// <summary>Zona horaria IANA con la que el front muestra las fechas.</summary>
    public string TimeZoneId { get; private set; } = DefaultTimeZoneId;

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public Guid? DeletedBy { get; private set; }

    /// <summary>
    /// Una cuenta nueva, con al menos un correo o un número. El nombre de usuario es el Id, así cambiar el correo o el
    /// número no altera la identidad de la cuenta. Cada confirmación vale solo si su dato está.
    /// </summary>
    public static ApplicationUser Create(
        string? email,
        bool emailConfirmed,
        string? phoneNumber,
        bool phoneNumberConfirmed,
        string? displayName,
        string culture,
        string timeZoneId = DefaultTimeZoneId)
    {
        AccountRules.EnsureHasContact(email is not null, phoneNumber is not null);

        var user = new ApplicationUser
        {
            Email = email,
            EmailConfirmed = email is not null && emailConfirmed,
            PhoneNumber = phoneNumber,
            PhoneNumberConfirmed = phoneNumber is not null && phoneNumberConfirmed,
        };

        user.UserName = user.Id.ToString("D", CultureInfo.InvariantCulture);
        user.Rename(displayName);
        user.UpdatePreferences(culture, timeZoneId);
        return user;
    }

    /// <summary>
    /// Cambia el nombre. Los nombres que se tipean ya los validó HTTP y los que llegan de afuera se recortaron en la
    /// entrada (<see cref="AccountRules.FitExternalDisplayName"/>): uno más largo que la columna es un bug.
    /// </summary>
    public void Rename(string? displayName)
    {
        if (!AccountRules.IsValidDisplayName(displayName))
        {
            throw new ArgumentException(
                $"The display name exceeds {AccountRules.DisplayNameMaxLength} characters.", nameof(displayName));
        }

        DisplayName = displayName;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>El idioma y la zona horaria, ya validados por quien los recibe.</summary>
    public void UpdatePreferences(string culture, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(culture.Length, AccountRules.CultureMaxLength, nameof(culture));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            timeZoneId.Length, AccountRules.TimeZoneIdMaxLength, nameof(timeZoneId));

        Culture = culture;
        TimeZoneId = timeZoneId;
    }

    /// <summary>
    /// Deshace el borrado lógico y deja la cuenta como recién dada de alta: activa, con el nombre nuevo y sin intentos
    /// fallidos ni bloqueo. Lo usa el alta de usuarios cuando el correo ya tuvo una cuenta.
    /// </summary>
    public void Restore(string? displayName)
    {
        Rename(displayName);
        IsDeleted = false;
        DeletedAtUtc = null;
        DeletedBy = null;
        IsActive = true;
        AccessFailedCount = 0;
        LockoutEnd = null;
    }
}
