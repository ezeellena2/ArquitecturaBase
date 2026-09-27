using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

/// <summary>
/// Corre el trabajo como UnitOfWork, con la misma regla de confirmación (<see cref="CommitPolicyExtensions.Commits"/>),
/// y cuenta qué pasó. Lo que el test quiere mirar "al confirmar" lo toma <see cref="OnCommit"/>, en el momento en que la
/// unidad real baja los cambios.
/// </summary>
internal sealed class FakeUnitOfWork(List<string>? events = null) : IUnitOfWork
{
    /// <summary>Cuántas veces se abrió el límite. En 0 significa que ni se abrió, por ejemplo con un ValidationError.</summary>
    public int Transactions { get; private set; }

    /// <summary>Cuántas veces se llegó al commit, contando la que falla por <see cref="CommitFailure"/>.</summary>
    public int Commits { get; private set; }

    /// <summary>Cuántas veces se deshizo: un Result fallido con OnSuccess, una excepción del trabajo o un commit fallido.</summary>
    public int Rollbacks { get; private set; }

    public CommitPolicy? LastPolicy { get; private set; }

    public bool InTransaction { get; private set; }

    /// <summary>Corre justo antes de confirmar: acá se sacan las fotos (lo enviado, las auditorías, el código gastado).</summary>
    public Action? OnCommit { get; init; }

    /// <summary>Si no es null, el commit falla con esta excepción después de <see cref="OnCommit"/>.</summary>
    public Exception? CommitFailure { get; set; }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CommitPolicy policy, CancellationToken cancellationToken)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown commit policy.");
        }

        if (InTransaction)
        {
            throw new InvalidOperationException("ExecuteInTransactionAsync does not nest.");
        }

        Transactions++;
        LastPolicy = policy;
        InTransaction = true;

        try
        {
            TResult result;

            try
            {
                result = await work(cancellationToken);
            }
            catch
            {
                Rollbacks++;
                throw;
            }

            if (!policy.Commits(result))
            {
                Rollbacks++;
                return result;
            }

            Commits++;
            events?.Add("commit");
            OnCommit?.Invoke();

            if (CommitFailure is { } failure)
            {
                Rollbacks++;
                throw failure;
            }

            return result;
        }
        finally
        {
            InTransaction = false;
        }
    }
}
