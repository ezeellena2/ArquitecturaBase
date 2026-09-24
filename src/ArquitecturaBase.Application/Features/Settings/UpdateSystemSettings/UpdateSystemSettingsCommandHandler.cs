using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;

internal sealed class UpdateSystemSettingsCommandHandler(
    ISystemSettingsRepository repository,
    ISystemSettingsReader reader)
    : ICommandHandler<UpdateSystemSettingsCommand>
{
    public async Task<Result> Handle(UpdateSystemSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await repository.GetAsync(cancellationToken);

        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        settings.SetRegistrationMode(command.RegistrationMode);

        // El caché se descarta acá y UnitOfWorkDecorator guarda enseguida: el cambio vale para el ingreso
        // siguiente, sin reiniciar la Api (sección 5 del spec de la Fase 4).
        await reader.InvalidateAsync(cancellationToken);

        return Result.Success();
    }
}
