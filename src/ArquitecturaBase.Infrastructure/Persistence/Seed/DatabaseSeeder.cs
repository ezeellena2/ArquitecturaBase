using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Corre los tres seeders en un solo límite (IUnitOfWork.ExecuteInTransactionAsync con OnSuccess) y en fila entre
/// réplicas: lo primero que hace adentro es tomar el lock <see cref="AdvisoryLockKeys.Seed"/>, así dos réplicas que
/// arrancan juntas siembran una después de la otra, y la segunda ve lo que la primera confirmó. Si un seeder lanza, se
/// deshace todo: no quedan roles sin el cliente web ni una fila de ajustes sola. Es la única pieza fuera de
/// Application/Services que abre un límite (ADR 0001, enmienda del 2026-09-28): el seed no es un caso de uso, no tiene
/// contrato en Interfaces/Services y sus errores son excepciones, así que el trabajo devuelve Result.Success() solo
/// porque el límite pide un Result.
/// </summary>
/// <remarks>
/// Adentro, cada seeder guarda así: RoleManager y UserManager autoguardan sobre este mismo contexto scoped, con un
/// savepoint por guardado, como los casos de uso desde la Etapa 1; los managers de OpenIddict guardan también sobre él y
/// no abren transacción propia (FindBy*, CreateAsync y UpdateAsync; verificado en OpenIddict.EntityFrameworkCore 7.7.1);
/// SystemSettingsSeeder solo agrega la fila, que baja con el guardado siguiente del mismo contexto o con el final del
/// límite.
/// </remarks>
internal sealed class DatabaseSeeder(
    IUnitOfWork unitOfWork,
    ApplicationDbContext dbContext,
    RoleSeeder roleSeeder,
    SystemSettingsSeeder systemSettingsSeeder,
    OpenIddictSeeder openIddictSeeder)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.Seed], ct);

                await roleSeeder.SeedAsync(ct);
                await systemSettingsSeeder.SeedAsync(ct);
                await openIddictSeeder.SeedAsync(ct);

                return Result.Success();
            },
            CommitPolicy.OnSuccess,
            cancellationToken);
    }
}
