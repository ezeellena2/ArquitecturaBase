namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

/// <summary>
/// Lo que exigen los locks de producción desde el final de la Etapa 1: una transacción abierta. Un doble de repositorio
/// que recibe <c>() =&gt; unitOfWork.InTransaction</c> lanza si se toma un lock fuera del límite; con null no controla
/// nada, así los tests de los helpers sueltos siguen igual.
/// </summary>
internal static class TransactionGuard
{
    public static void Require(Func<bool>? inTransaction)
    {
        if (inTransaction is not null && !inTransaction())
        {
            throw new InvalidOperationException("A lock was taken outside IUnitOfWork.ExecuteInTransactionAsync.");
        }
    }
}
