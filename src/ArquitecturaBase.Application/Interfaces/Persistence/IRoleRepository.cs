namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Escrituras de roles sobre Identity. RoleManager guarda el rol y cada claim por separado. Cada operación exige la
/// transacción del caso de uso (<see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/>): sin ella lanza
/// <see cref="InvalidOperationException"/>, porque un rol podría quedar con parte de sus permisos. La edición avanza la
/// revisión del rol sólo si cambia el nombre, la descripción o los permisos.
/// </summary>
public interface IRoleRepository
{
    Task<Guid> CreateAsync(
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        Guid roleId,
        string name,
        string? description,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Bloquea la fila del rol (FOR UPDATE) hasta el final de la transacción, antes de leerlo para editarlo o borrarlo
    /// (N2). FOR UPDATE choca también con <see cref="LockForAssignmentAsync"/>: el borrado y una asignación del mismo rol
    /// pasan de a uno, y el segundo ve lo que confirmó el primero (el borrado cuenta la cuenta nueva y responde
    /// <c>HasUsers</c>; la asignación no encuentra el rol). Sin la transacción lanza <see cref="InvalidOperationException"/>.
    /// </summary>
    Task LockAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Bloquea, para asignarlos, los roles con esos nombres (FOR KEY SHARE, el lock de una clave foránea): no frena a otra
    /// asignación ni a la edición del rol, pero sí a su borrado (<see cref="LockAsync"/>). Quien llama comprueba después que
    /// existan, así un rol que se borró mientras esperaba da <c>Roles.Role.NotFound</c> y no un 500 ni una asignación
    /// borrada en cascada. Sin la transacción lanza <see cref="InvalidOperationException"/>.
    /// </summary>
    Task LockForAssignmentAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken);
}
