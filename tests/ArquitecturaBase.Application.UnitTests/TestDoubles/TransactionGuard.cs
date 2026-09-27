namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

/// <summary>
/// Lo que exigen los locks y las escrituras de cuentas de producción desde el final de la Etapa 1: una transacción
/// abierta. Un doble de repositorio que recibe <c>() =&gt; unitOfWork.InTransaction</c> lanza si se toma un lock o se
/// escribe fuera del límite; con null no controla nada, así los tests de los helpers sueltos siguen igual.
/// </summary>
internal static class TransactionGuard
{
    public static void Require(Func<bool>? inTransaction)
    {
        if (inTransaction is not null && !inTransaction())
        {
            throw new InvalidOperationException(
                "A lock or a write ran outside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }

    /// <summary>
    /// Lo contrario de <see cref="Require"/>: lo que corre después del commit (la cookie de la aplicación) lanza adentro
    /// de un límite, como ISignInService.SignInAsync en producción. Con null no controla nada.
    /// </summary>
    public static void RequireNone(Func<bool>? inTransaction)
    {
        if (inTransaction is not null && inTransaction())
        {
            throw new InvalidOperationException(
                "This operation runs after the use case commits: call it outside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }
}
