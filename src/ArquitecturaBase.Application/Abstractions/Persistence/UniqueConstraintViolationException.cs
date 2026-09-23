namespace ArquitecturaBase.Application.Abstractions.Persistence;

/// <summary>
/// Otro pedido guardó primero una fila con la misma clave única. La lanza <see cref="IUnitOfWork.SaveChangesAsync"/>
/// y no se guardó nada del caso de uso. Es una falla de infraestructura, no una regla de negocio: la mayoría de los
/// casos de uso la dejan llegar al 500 genérico; el webhook de WhatsApp la trata como un aviso repetido.
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
