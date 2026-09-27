using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>La regla de <see cref="CommitPolicy"/>: qué políticas existen y con cuáles se confirma un Result.</summary>
public static class CommitPolicyExtensions
{
    /// <summary>
    /// Si la transacción se confirma con <paramref name="result"/>. Es la única definición de la regla: la usan
    /// UnitOfWork y los dobles de prueba, así ningún test puede afirmar otra semántica que la de producción.
    /// </summary>
    public static bool Commits(this CommitPolicy policy, Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess || policy == CommitPolicy.OnAnyResult;
    }

    /// <summary>
    /// Lanza <see cref="ArgumentOutOfRangeException"/> con una política que no es ninguna de las declaradas: fuera de
    /// rango, <see cref="Commits"/> se comportaría como <see cref="CommitPolicy.OnSuccess"/> sin avisar. UnitOfWork y
    /// los dobles de prueba lo llaman antes de abrir nada, así el chequeo también tiene una sola definición.
    /// </summary>
    public static void ThrowIfUndefined(this CommitPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown commit policy.");
        }
    }
}
