using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Services.Settings;

public sealed class SystemSettingsService(
    ISystemSettingsRepository repository,
    ISystemSettingsReader reader,
    IUnitOfWork unitOfWork,
    ServiceRequestValidator<UpdateSystemSettingsRequest> validator) : ISystemSettingsService
{
    /// <summary>El panel lee la fila guardada, no la lectura cacheada del camino de ingreso.</summary>
    public async Task<Result<SystemSettingsResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.GetAsync(cancellationToken);

        return settings is null
            ? SettingsErrors.NotFound
            : new SystemSettingsResponse(settings.RegistrationMode);
    }

    public async Task<Result> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = await validator.ValidateAsync(request, cancellationToken);

        if (validationError is not null)
        {
            return validationError;
        }

        var settings = await repository.GetAsync(cancellationToken);

        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        settings.SetRegistrationMode(request.RegistrationMode);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await reader.InvalidateAsync(cancellationToken);

        return Result.Success();
    }
}
