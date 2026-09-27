namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Qué hace <see cref="IUnitOfWork"/> con un <c>Result</c> fallido. Un éxito se confirma siempre y una excepción deshace
/// siempre: la política decide solo qué pasa con un error de negocio, y cada servicio la escribe en su llamada ("cada
/// servicio define expresamente cuándo guarda").
/// </summary>
public enum CommitPolicy
{
    /// <summary>
    /// Solo se confirma el éxito. Un Result fallido deshace todo, también las escrituras que ya se guardaron adentro (los
    /// autoguardados de Identity y cualquier otra escritura inmediata), y suelta los locks en el acto. Es la política de
    /// casi todos los casos de uso, y el valor por defecto.
    /// </summary>
    OnSuccess = 0,

    /// <summary>
    /// Se confirma con cualquier Result, exitoso o fallido, porque el caso de uso tiene que dejar rastro de un intento que
    /// falló: el intento de un código, un código o un enlace gastado, un bloqueo, una auditoría. Una excepción igual
    /// deshace todo. Quien la elige dice en su llamada qué rastro deja.
    /// </summary>
    OnAnyResult = 1,
}
