using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class UserManagerExtensions
{
    /// <summary>
    /// La única forma de cargar por Id una cuenta no borrada para modificarla con UserManager, seguida por EF. Va
    /// siempre a la base con el filtro global de borrados y el token de quien llama. Si EF ya sigue la fila en este
    /// scope, devuelve esa misma instancia sin refrescarla (identity resolution), con su ConcurrencyStamp: por eso se
    /// lee después de tomar los locks. El chequeo de IsDeleted en memoria cubre una instancia seguida que se marcó
    /// borrada y no llegó a guardarse. Sin cuenta, lanza: quien llama ya la buscó.
    /// </summary>
    /// <remarks>
    /// Dos cargas quedan afuera a propósito, porque buscan otra cosa: UserRepository.RestoreAsync carga la cuenta
    /// borrada (IgnoreQueryFilters sobre el filtro de borrados) y RoleSeeder busca al administrador inicial por su
    /// correo (FindByEmailAsync) para darle el rol Admin.
    /// </remarks>
    public static async Task<ApplicationUser> RequireUserAsync(
        this UserManager<ApplicationUser> userManager, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userManager);

        return await userManager.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            is { IsDeleted: false } user
            ? user
            : throw new InvalidOperationException("The user does not exist.");
    }
}
