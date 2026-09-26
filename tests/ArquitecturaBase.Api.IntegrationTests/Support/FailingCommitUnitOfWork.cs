using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>La falla que simula el commit.</summary>
internal sealed class ExpectedCommitFailure : Exception;

/// <summary>Lo que un test quiere mirar de un commit que falla.</summary>
internal sealed class CommitFailureProbe
{
    /// <summary>Corre dentro de la transacción, con todo ya guardado, justo antes de fallar.</summary>
    public Func<ApplicationDbContext, CancellationToken, Task>? BeforeFailing { get; init; }

    /// <summary>Si al salir la transacción ya estaba deshecha y el tracker vacío: el rollback lo hizo producción.</summary>
    public bool RolledBackBeforeLeaving { get; set; }
}

/// <summary>
/// La unidad de trabajo de producción, pero el commit falla justo después de guardar los cambios. Lo que se prueba es el
/// rollback explícito de UnitOfWork, no el que hace el Dispose del scope: la excepción sale del trabajo, y la unidad real
/// deshace, vacía el tracker y la relanza sin traducir.
/// </summary>
internal sealed class FailingCommitUnitOfWork(UnitOfWork inner, ApplicationDbContext db, CommitFailureProbe probe) : IUnitOfWork
{
    public static void Replace(IServiceCollection services, CommitFailureProbe probe)
    {
        services.RemoveAll<IUnitOfWork>();
        services.AddScoped<IUnitOfWork>(provider => new FailingCommitUnitOfWork(
            ActivatorUtilities.CreateInstance<UnitOfWork>(provider), provider.GetRequiredService<ApplicationDbContext>(), probe));
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CommitPolicy policy, CancellationToken cancellationToken)
        where TResult : Result
    {
        try
        {
            return await inner.ExecuteInTransactionAsync(async ct =>
            {
                var result = await work(ct);

                if (policy.Commits(result))
                {
                    Assert.NotNull(db.Database.CurrentTransaction);
                    await db.SaveChangesAsync(ct);

                    if (probe.BeforeFailing is { } check)
                    {
                        await check(db, ct);
                    }

                    throw new ExpectedCommitFailure();
                }

                return result;
            }, policy, cancellationToken);
        }
        catch (ExpectedCommitFailure)
        {
            probe.RolledBackBeforeLeaving = db.Database.CurrentTransaction is null && !db.ChangeTracker.Entries().Any();
            throw;
        }
    }

    // En retiro (Etapa 1): lo borra la Tarea 21.
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The services that use this double already run inside ExecuteInTransactionAsync.");
}
