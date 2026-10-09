using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Application.Interfaces.Channels;
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
    ISystemSettingsReader reader,
    IEnumerable<IClientCapabilitySource>? capabilitySources = null) : ISystemSettingsService
{
    /// <summary>El panel lee la fila guardada, no la lectura cacheada del camino de ingreso.</summary>
    public Task<Result<SystemSettingsResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<SystemSettingsResponse>>(logger, "GetSystemSettings", async () =>
        {
            var settings = await repository.GetAsync(cancellationToken);

            return settings is null
                ? SettingsErrors.NotFound
                : new SystemSettingsResponse(settings.RegistrationMode, settings.DefaultCulture,
                    settings.DefaultTimeZoneId, settings.DefaultPageSize, settings.Revision,
                    new SystemAppearanceResponse(settings.PrimaryColor, settings.MenuColor, settings.HeaderColor, settings.AlternateRowColor));
        });

    public Task<Result<SystemPresentationResponse>> GetPresentationAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<SystemPresentationResponse>>(logger, "GetSystemPresentation",
            async () =>
            {
                var features = (capabilitySources ?? []).SelectMany(source => source.InstalledFeatures).ToArray();
                if (features.Any(string.IsNullOrWhiteSpace) || features.Distinct(StringComparer.Ordinal).Count() != features.Length)
                {
                    throw new InvalidOperationException("Installed client capabilities must be nonempty and unique.");
                }

                var presentation = await reader.FindPresentationAsync(cancellationToken);
                return presentation with { InstalledFeatures = [.. features.Order(StringComparer.Ordinal)] };
            });

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

                var appearance = request.Appearance is { } colors
                    ? new AppearancePalette(colors.PrimaryColor, colors.MenuColor, colors.HeaderColor, colors.AlternateRowColor)
                    : null;
                return settings.Update(request.DefaultCulture, request.DefaultTimeZoneId,
                    request.DefaultPageSize, request.RegistrationMode, appearance);
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
