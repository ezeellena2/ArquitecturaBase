using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Crea la fila de ajustes con el valor de Registration:Mode. Si ya existe, manda la base: un despliegue nunca
/// pisa lo que se configuró desde el panel (sección 5 del spec de la Fase 4). No guarda: corre dentro del límite de
/// <see cref="DatabaseSeeder"/>, y la fila baja con el guardado siguiente del mismo contexto o con el final del límite.
/// </summary>
internal sealed class SystemSettingsSeeder(
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
    }
}
