using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Settings;

internal sealed class SystemSettingsService(
    ISystemSettingsRepository repository,
    ISystemSettingsCache cache,
    IUnitOfWork unitOfWork,
    IRequestValidator validator,
    ILogger<SystemSettingsService> logger,
    ISystemSettingsReader reader) : ISystemSettingsService
{
    /// <summary>El panel lee la fila guardada, no la lectura cacheada del camino de ingreso.</summary>
    public Task<Result<SystemSettingsResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<SystemSettingsResponse>>(logger, "GetSystemSettings", async () =>
        {
            var settings = await repository.GetAsync(cancellationToken);

            return settings is null
                ? SettingsErrors.NotFound
                : new SystemSettingsResponse(settings.RegistrationMode, settings.DefaultCulture,
                    settings.DefaultTimeZoneId, settings.DefaultPageSize, settings.Revision);
        });

    public Task<Result<SystemPresentationResponse>> GetPresentationAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<SystemPresentationResponse>>(logger, "GetSystemPresentation",
            async () => await reader.FindPresentationAsync(cancellationToken));

    public Task<Result> UpdateSectionAsync(UpdateSystemSettingsSectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result>(logger, "UpdateSystemSettingsSection", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await repository.LockAsync(ct);
                var settings = await repository.GetAsync(ct);
                if (settings is null)
                {
                    return Result.Failure(SettingsErrors.NotFound);
                }

                if (settings.Revision != request.ExpectedRevision)
                {
                    return Result.Failure(SettingsErrors.RevisionConflict);
                }

                if (request.DefaultCulture is { } culture)
                {
                    return settings.SetDefaultCulture(culture);
                }

                if (request.DefaultPageSize is { } size)
                {
                    return settings.SetDefaultPageSize(size);
                }

                if (request.DefaultTimeZoneId is { } zone)
                {
                    settings.SetDefaultTimeZoneId(zone);
                }
                else if (request.RegistrationMode is { } mode)
                {
                    settings.SetRegistrationMode(mode);
                }

                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);

            if (result.IsSuccess)
            {
                await cache.InvalidateAsync(cancellationToken);
            }

            return result;
        });
    }

    public Task<Result> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "UpdateSystemSettings", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var result = await unitOfWork.ExecuteInTransactionAsync(
                ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

            // Después del commit y solo si se confirmó: invalidar antes dejaría que una lectura concurrente vuelva a
            // cachear el modo viejo. Que la fábrica no vea lo no confirmado lo garantiza el lector, que lee en su propio
            // scope.
            if (result.IsSuccess)
            {
                await cache.InvalidateAsync(cancellationToken);
            }

            return result;
        });
    }

    private async Task<Result> UpdateCoreAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken)
    {
        await repository.LockAsync(cancellationToken);
        var settings = await repository.GetAsync(cancellationToken);
        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        settings.SetRegistrationMode(request.RegistrationMode);
        return Result.Success();
    }
}
