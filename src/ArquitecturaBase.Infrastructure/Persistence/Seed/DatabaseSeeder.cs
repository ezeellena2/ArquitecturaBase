using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Corre los seeders del núcleo y participantes técnicos opcionales en un solo límite (IUnitOfWork.ExecuteInTransactionAsync con OnSuccess) y en fila entre
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
/// Después del commit descarta en Redis sólo los ajustes: los permisos no tienen caché (N9 y N33, 2026-11-16). Sin Redis el
/// arranque sigue: lo que Redis conserve de antes vence con su TTL de 60 segundos.
/// </remarks>
internal sealed partial class DatabaseSeeder(
    IUnitOfWork unitOfWork,
    ApplicationDbContext dbContext,
    RoleSeeder roleSeeder,
    SystemSettingsSeeder systemSettingsSeeder,
    OpenIddictSeeder openIddictSeeder,
    DataProtectionKeySeeder dataProtectionKeySeeder,
    ISystemSettingsCache settingsCache,
    IEnumerable<IDatabaseSeedParticipant> participants,
    ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.Seed], ct);
                await dataProtectionKeySeeder.SeedAsync(ct);

                await roleSeeder.SeedAsync(ct);
                await systemSettingsSeeder.SeedAsync(ct);
                await openIddictSeeder.SeedAsync(ct);
                foreach (var participant in participants) await participant.SeedAsync(ct);

                return Result.Success();
            },
            CommitPolicy.OnSuccess,
            cancellationToken);

        // Redis may survive the process. Discard only after the complete seed has committed.
        try
        {
            await settingsCache.InvalidateAsync(cancellationToken);
        }
        catch (Exception exception) when (RedisCache.IsUnavailable(exception))
        {
            SettingsCacheNotDiscarded(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Redis did not respond after the seed: the settings it kept, if any, expire with their TTL.")]
    private static partial void SettingsCacheNotDiscarded(ILogger logger, Exception exception);
}
