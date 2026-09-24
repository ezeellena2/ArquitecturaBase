using ArquitecturaBase.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// Traduce el choque con un índice único de Postgres (23505) a la excepción que conoce Application. La usan la unidad
/// de trabajo y los guardados que hace Identity por su cuenta.
/// </summary>
internal static class UniqueViolations
{
    /// <summary>
    /// La <see cref="UniqueConstraintViolationException"/> de <paramref name="exception"/>, o null si el guardado falló
    /// por otra cosa. Sin los valores repetidos: Npgsql tampoco los pone en el mensaje si no se lo piden.
    /// </summary>
    public static UniqueConstraintViolationException? Translate(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique }
            ? new UniqueConstraintViolationException(
                $"Another request saved a row with the same unique key first ({unique.ConstraintName}).", exception)
            {
                ConstraintName = unique.ConstraintName,
            }
            : null;
}
