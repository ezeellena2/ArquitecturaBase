using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>Conversión compartida por el adaptador de sesión y el repositorio de cuentas.</summary>
internal static class ApplicationUserMapper
{
    public static UserAccount? ToAccountOrNull(ApplicationUser? user) =>
        user is null ? null : ToAccount(user);

    public static UserAccount ToAccount(ApplicationUser user) =>
        new(
            user.Id,
            user.Email,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.DisplayName,
            user.Culture,
            user.TimeZoneId,
            user.IsActive);
}
