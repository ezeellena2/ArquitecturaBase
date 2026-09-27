using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Lo técnico del ingreso sobre ASP.NET Core Identity y OpenIddict: el bloqueo por intentos fallidos, la cookie de la
/// aplicación, la cookie del proveedor externo y el cierre de todas las sesiones de una cuenta. No tiene datos de
/// cuentas: se leen con <see cref="IUserReader"/> y se escriben con <see cref="IUserRepository"/>, incluido el vínculo con
/// el proveedor externo. Lo implementa Infrastructure: UserManager y SignInManager no salen de ahí.
/// </summary>
/// <remarks>
/// <para>Cada miembro sigue una de dos reglas, y UnitOfWorkTransactionTests exige que todo miembro nuevo declare la
/// suya:</para>
/// <list type="bullet">
/// <item><b>Escribe</b> (<see cref="RegisterFailedAttemptAsync"/>, <see cref="ResetFailedAttemptsAsync"/>,
/// <see cref="RevokeSessionsAsync"/>): exige la transacción del caso de uso
/// (<see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>) y, sin ella, lanza
/// <see cref="InvalidOperationException"/> antes de tocar nada.</item>
/// <item><b>En cualquier lado</b> (<see cref="IsLockedOutAsync"/>, <see cref="SignInAsync"/>,
/// <see cref="GetExternalLoginAsync"/>, <see cref="SignOutExternalAsync"/>): leen, o tocan solo las cookies de la
/// petición.</item>
/// </list>
/// <para>Los miembros que reciben un userId cargan la cuenta no borrada con ese Id, siempre en la base. Si no existe,
/// lanzan <see cref="InvalidOperationException"/>, porque quien llama ya la buscó. Si la cuenta ya está seguida en el
/// scope, usan esa misma instancia sin refrescarla: quien escribe después de un lock tiene que haberla leído después del
/// lock.</para>
/// <para>El bot de WhatsApp usa solo <see cref="IsLockedOutAsync"/>: un mensaje nunca abre una sesión.</para>
/// </remarks>
public interface ISignInService
{
    /// <summary>Si Identity tiene bloqueada la cuenta por intentos fallidos, según su propio reloj. No escribe.</summary>
    Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Suma un intento fallido; al llegar al máximo (Authentication:LoginCode:LockoutMaxFailedAttempts), Identity bloquea
    /// la cuenta un tiempo. Solo lo suma el ingreso por código: confirmar un destino desde el perfil no es un ingreso.
    /// Exige la transacción, que el ingreso confirma con <see cref="CommitPolicy.OnAnyResult"/> aunque falle.
    /// </summary>
    Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Vuelve a cero los intentos fallidos. Exige la transacción: va adentro, antes del commit.</summary>
    Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Renueva el security stamp, con lo que la cookie de Identity deja de valer en la próxima petición (ValidationInterval
    /// en cero), y revoca primero las autorizaciones y después los tokens de OpenIddict de la cuenta. Exige la transacción:
    /// las revocaciones son UPDATE inmediatos y, sin ella, se confirmarían sueltas. Los enlaces pendientes los invalida
    /// <c>AccountAccessRevoker</c>, que es su único llamador.
    /// </summary>
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Escribe en la respuesta la cookie persistente de la aplicación. No escribe en la base. La llaman solo los puntos de
    /// entrada del ingreso (código, enlace y Google); el código y Google, después del commit y solo con un Result exitoso.
    /// </summary>
    Task SignInAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// El resultado del proveedor externo, leído de la cookie externa, o null si no hay un ingreso externo en curso.
    /// </summary>
    Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken);

    /// <summary>Borra la cookie externa, que sirve para un solo callback.</summary>
    Task SignOutExternalAsync(CancellationToken cancellationToken);
}
