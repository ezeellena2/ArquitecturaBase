using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Crea la fila de ajustes con el valor de Registration:Mode. Si ya existe, manda la base: un despliegue nunca
/// pisa lo que se configuró desde el panel (sección 5 del spec de la Fase 4).
/// </summary>
internal sealed class SystemSettingsSeeder(
    ApplicationDbContext dbContext,
    ISystemSettingsRepository repository,
    IOptions<RegistrationOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await repository.GetAsync(cancellationToken) is not null)
        {
            return;
        }

        repository.Add(SystemSettings.Create(options.Value.Mode));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
