using System.Globalization;
using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Infrastructure.Persistence.Extensions;

/// <summary>
/// Todas las claves de pg_advisory_xact_lock del núcleo (un módulo opcional guarda las suyas en su propio tipo de
/// claves). El texto ES el lock: Postgres toma hashtextextended(clave, 0). Cambiar un
/// prefijo o el formato de un id deja de poner en fila a quien use el texto viejo, por ejemplo la versión anterior de la
/// Api durante un despliegue, o separa casos de uso que hoy se esperan entre sí (el verify, Google, el alta del
/// administrador y el perfil comparten login-code:). AcquireAdvisoryLocksAsync ordena las claves de una misma llamada en
/// orden ordinal. El orden entre llamadas lo decide quien llama y no se cambia: contactos antes que cuenta, el
/// login-code: del correo antes que el del número, en DOS llamadas, y users:admins al final. En una sola, el orden ordinal pondría '+54…' antes
/// que el correo. AdvisoryLockKeysTests fija cada texto.
/// </summary>
internal static class AdvisoryLockKeys
{
    /// <summary>"login-code:" + LoginCodeDestination.Value: el correo normalizado (Email.Value) o el E.164 con el 9.</summary>
    public static string LoginCode(LoginCodeDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return "login-code:" + destination.Value;
    }

    /// <summary>"login-link:" + el Id de la cuenta en formato N. Es el lock "de la cuenta".</summary>
    public static string LoginLink(Guid userId) => "login-link:" + userId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>"user-invitation:" + el Id de la cuenta en formato N.</summary>
    public static string UserInvitation(Guid userId) =>
        "user-invitation:" + userId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// "external-login:" + proveedor + ":" + clave del proveedor. Solo la toma Google, en la misma llamada que el
    /// login-code: del correo. Por el orden ordinal se toma primero, y como nadie más la pide, no hay ciclo.
    /// </summary>
    public static string ExternalLogin(string provider, string providerKey) =>
        "external-login:" + provider + ":" + providerKey;

    /// <summary>
    /// "users:admins". Uno solo para todo el sistema: pone en fila lo que puede dejarlo sin administradores activos
    /// (desactivar, eliminar o sacarle el rol Admin a uno). Lo toma UserGuard, último, después de los locks de contactos
    /// y de cuenta del caso de uso, y antes de contar. Es una propiedad por lo mismo que <see cref="Seed"/>.
    /// </summary>
    public static string Admins => "users:admins";

    /// <summary>
    /// "seed:database". Lo toma solo DatabaseSeeder, primero y solo, al abrir su límite: pone en fila el seed de las
    /// réplicas que arrancan juntas. Es una propiedad y no una constante: una constante se copiaría al IL de quien la
    /// usa, y el texto del lock tiene que vivir solo acá (TransactionBoundaryTests).
    /// </summary>
    public static string Seed => "seed:database";
}
