using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// La cuenta que dejó <see cref="UserContactLinker.CreateOrRestoreAsync"/> y, si era una borrada, cómo estaba antes de
/// restaurarla (<paramref name="Deleted"/>): la historia de los cambios administrativos compara con ella el correo y el
/// número que reemplazó o quitó el alta (N4 y N13).
/// </summary>
internal sealed record CreatedAccount(UserAccount Account, UserAccount? Deleted)
{
    public bool Restored => Deleted is not null;
}
