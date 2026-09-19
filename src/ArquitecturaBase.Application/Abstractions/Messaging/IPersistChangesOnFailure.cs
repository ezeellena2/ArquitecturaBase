namespace ArquitecturaBase.Application.Abstractions.Messaging;

/// <summary>
/// Marca un comando cuyos cambios se guardan aunque el resultado sea un error. Por ejemplo, los intentos fallidos
/// de un código de ingreso y su auditoría tienen que quedar registrados.
/// </summary>
public interface IPersistChangesOnFailure;
