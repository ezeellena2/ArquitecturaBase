using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

/// <summary>
/// ISignInService en memoria: registra lo que hicieron los casos de uso con la sesión y el bloqueo. Su estado va por
/// userId y no depende de las cuentas (InMemoryUserAccounts). Con una lista de eventos, anota "sign-in" en la misma lista
/// en la que FakeUnitOfWork anota "commit": así un test fija en qué orden pasaron.
/// </summary>
/// <remarks>
/// A diferencia de producción, no lanza con una cuenta borrada o que no existe, porque no conoce las cuentas:
/// IsLockedOutAsync devuelve false. Un test que dependa de ese InvalidOperationException va en integración
/// (SignInServiceTests.Operations_reject_a_deleted_or_missing_account).
/// </remarks>
internal sealed class FakeSignInService(List<string>? events = null) : ISignInService
{
    public Dictionary<Guid, int> FailedAttempts { get; } = [];

    public HashSet<Guid> LockedOutUsers { get; } = [];

    public List<Guid> SignedInUsers { get; } = [];

    /// <summary>A quiénes se les cortó el acceso ya emitido.</summary>
    public List<Guid> RevokedUsers { get; } = [];

    public ExternalLogin? PendingExternalLogin { get; set; }

    public bool ExternalSignedOut { get; private set; }

    /// <summary>
    /// Si no es null, las escrituras (los intentos fallidos y el cierre de sesiones) lanzan fuera de la transacción, y
    /// <see cref="SignInAsync"/> lanza adentro, como en producción (ver <see cref="TransactionGuard"/>).
    /// </summary>
    public Func<bool>? InTransaction { get; set; }

    /// <summary>
    /// Si no es null, <see cref="SignInAsync"/> falla con esta excepción, como una cookie que no se pudo escribir después
    /// del commit. La llamada igual se anota como "sign-in" en la lista de eventos (el orden respecto del commit es lo que
    /// importa), pero no en <see cref="SignedInUsers"/>.
    /// </summary>
    public Exception? SignInFailure { get; set; }

    public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(LockedOutUsers.Contains(userId));

    public Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        FailedAttempts[userId] = FailedAttempts.GetValueOrDefault(userId) + 1;

        return Task.CompletedTask;
    }

    public Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        FailedAttempts[userId] = 0;

        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.Require(InTransaction);
        RevokedUsers.Add(userId);

        return Task.CompletedTask;
    }

    public Task SignInAsync(Guid userId, CancellationToken cancellationToken)
    {
        TransactionGuard.RequireNone(InTransaction);
        events?.Add("sign-in");

        if (SignInFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        SignedInUsers.Add(userId);

        return Task.CompletedTask;
    }

    public Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PendingExternalLogin);

    public Task SignOutExternalAsync(CancellationToken cancellationToken)
    {
        ExternalSignedOut = true;

        return Task.CompletedTask;
    }
}
