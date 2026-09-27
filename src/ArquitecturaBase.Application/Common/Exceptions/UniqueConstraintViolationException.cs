using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Common.Exceptions;

/// <summary>
/// Otro pedido guardó primero una fila con la misma clave única. La lanza
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}"/> después de deshacer la transacción, y en ese caso no se
/// guardó nada del caso de uso. También la lanzan los guardados de Identity que el repositorio traduce
/// (<see cref="IUserRepository.CreateUnverifiedAsync"/>, <see cref="IUserRepository.SetEmailAsync"/> y
/// <see cref="IUserRepository.SetPhoneAsync"/>): esos se pueden atrapar dentro del trabajo para devolver un Result,
/// porque el savepoint deshizo solo ese guardado. Es una falla de infraestructura, no una regla de negocio: la
/// mayoría de los casos de uso la dejan llegar al 500 genérico; el webhook de WhatsApp la trata como un aviso repetido.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException()
    {
    }

    public UniqueConstraintViolationException(string message)
        : base(message)
    {
    }

    public UniqueConstraintViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>El nombre del índice único, si la base lo dijo. Nunca los valores repetidos.</summary>
    public string? ConstraintName { get; init; }
}
