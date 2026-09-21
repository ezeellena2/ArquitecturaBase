using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.GetSystemSettings;

/// <summary>Lee la fila, no el caché: el panel tiene que mostrar lo que está guardado.</summary>
internal sealed class GetSystemSettingsQueryHandler(ISystemSettingsRepository repository)
    : IQueryHandler<GetSystemSettingsQuery, SystemSettingsResponse>
{
    public async Task<Result<SystemSettingsResponse>> Handle(GetSystemSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await repository.GetAsync(cancellationToken);

        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        return new SystemSettingsResponse(settings.RegistrationMode);
    }
}
