using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

public static class CommitPolicyExtensions
{
    /// <summary>
    /// Si la transacción se confirma con <paramref name="result"/>. Es la única definición de la regla: la usan UnitOfWork
    /// y los dobles de prueba, así ningún test puede afirmar otra semántica que la de producción.
    /// </summary>
    public static bool Commits(this CommitPolicy policy, Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess || policy == CommitPolicy.OnAnyResult;
    }
}
