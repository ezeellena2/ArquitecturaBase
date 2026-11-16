using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Crea la fila de ajustes con el valor de Registration:Mode. Si ya existe, manda la base: un despliegue nunca
/// pisa lo que se configuró desde el panel (sección 5 del spec de la Fase 4). No guarda: corre dentro del límite de
/// <see cref="DatabaseSeeder"/>, y la fila baja con el guardado siguiente del mismo contexto o con el final del límite. El
/// modo inicial deja su historia sin actor (N13).
/// </summary>
internal sealed class SystemSettingsSeeder(
    ISystemSettingsRepository repository,
    IOptions<RegistrationOptions> options,
    IAdministrativeChangeRepository changes,
    TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await repository.GetAsync(cancellationToken) is not null)
        {
            return;
        }

        var settings = SystemSettings.Create(options.Value.Mode);
        repository.Add(settings);
        changes.Add(AdministrativeChange.Create(
            AdministrativeChangeKind.RegistrationModeChanged, actorUserId: null, settings.Id,
            timeProvider.GetUtcNow().UtcDateTime, after: settings.RegistrationMode.ToString()));
    }
}
