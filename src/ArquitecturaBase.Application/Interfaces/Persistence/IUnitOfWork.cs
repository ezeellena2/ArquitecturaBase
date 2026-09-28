using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// El límite transaccional de un caso de uso y la única forma de guardar. Lo usa solo el punto de entrada del caso de uso,
/// o sea una clase que implementa un contrato de Interfaces/Services, una vez por cada método que escribe. La única
/// excepción con nombre es el seed de arranque de Infrastructure (ADR 0001, enmienda del 2026-09-28). Adentro van los
/// locks, las lecturas que deciden y todas las escrituras, también las que UserManager y RoleManager guardan por su
/// cuenta: lo hacen sobre el mismo contexto, dentro de esta transacción y con un savepoint cada una. La validación del
/// pedido va antes, y lo que depende del commit (invalidar un caché, la cookie de la aplicación) va después. Helpers,
/// repositorios y lectores nunca confirman.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Abre una transacción (READ COMMITTED), corre <paramref name="work"/> y después:
    /// <list type="bullet">
    /// <item>si <paramref name="policy"/> confirma el <see cref="Result"/> (<see cref="CommitPolicyExtensions.Commits"/>),
    /// baja lo que quedó seguido en un único guardado y confirma;</item>
    /// <item>si no lo confirma, deshace sin bajar nada y devuelve el mismo Result;</item>
    /// <item>si el trabajo, el guardado final o el commit lanzan una excepción (la cancelación incluida), deshace en el acto
    /// y la relanza. Si otro pedido guardó primero una fila con la misma clave única, la relanza como
    /// <see cref="UniqueConstraintViolationException"/>.</item>
    /// </list>
    /// Al deshacer suelta los locks antes de devolver o lanzar, y vacía el change tracker. No se anida ni adopta una
    /// transacción que no abrió: en los dos casos lanza <see cref="InvalidOperationException"/>. Nunca reintenta el
    /// trabajo, porque puede haber encolado un correo o un mensaje de WhatsApp.
    /// </summary>
    /// <param name="work">El caso de uso, ya validado. Casi siempre es un método privado <c>*CoreAsync</c> con el tipo
    /// de retorno escrito. Recibe el mismo token.</param>
    /// <param name="policy">Qué hacer con un Result fallido. No tiene valor por defecto, a propósito.</param>
    /// <param name="cancellationToken">El token del pedido: lo reciben el trabajo, el guardado final y el commit.</param>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CommitPolicy policy,
        CancellationToken cancellationToken)
        where TResult : Result;
}
