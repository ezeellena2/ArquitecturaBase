namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Qué hace <see cref="IUnitOfWork"/> con un <c>Result</c> fallido. Un éxito se confirma siempre y una excepción deshace
/// siempre: la política decide solo qué pasa con un error de negocio, y cada servicio la escribe en su llamada ("cada
/// servicio define expresamente cuándo guarda").
/// </summary>
public enum CommitPolicy
{
    /// <summary>
    /// Solo se confirma el éxito. Un Result fallido deshace todo, incluido lo que UserManager y RoleManager ya autoguardaron
    /// adentro y las revocaciones de OpenIddict, y suelta los locks en el acto. Es la política de casi todos los casos de uso.
    /// </summary>
    OnSuccess = 0,

    /// <summary>
    /// Se confirma con cualquier Result, exitoso o fallido, porque el caso de uso tiene que dejar rastro de un intento que
    /// falló: el intento de un código, un código o un enlace gastado, un bloqueo, una auditoría. Una excepción igual deshace
    /// todo. La usan solo el verify de código, el canje de enlace, Google y las dos confirmaciones del perfil.
    /// </summary>
    OnAnyResult = 1,
}
