using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Configuración › WhatsApp (RC3-A, 2026-11-22): the screen reads the settings (the values of before it until the first save) and saves
/// the three at once with the revision it read, 409 if another save came first, like the rest of the internal configuration (ADR 0018).
/// </summary>
internal sealed class WhatsAppSettingsService(IWhatsAppSettingsRepository settings, IWhatsAppSettingsReader reader, IRequestValidator validator,
    IUnitOfWork unitOfWork, ILogger<WhatsAppSettingsService> logger) : IWhatsAppSettingsService
{
    public Task<Result<WhatsAppSettingsResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppSettingsResponse>>(logger, "GetWhatsAppSettings", async () => await reader.FindAsync(cancellationToken));

    public Task<Result<WhatsAppSettingsResponse>> UpdateAsync(UpdateWhatsAppSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<WhatsAppSettingsResponse>>(logger, "UpdateWhatsAppSettings", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError) return validationError;
            var saved = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await settings.LockAsync(ct);
                var current = await settings.GetAsync(ct);
                var created = current is null;
                current ??= WhatsAppSettings.Create();
                var revision = current.EnsureRevision(request.ExpectedRevision);
                if (revision.IsFailure) return revision;
                var updated = current.Update(request.FlowSessionLifetimeHours, request.AllowedCountries, request.MessageRetentionDays);
                if (updated.IsFailure) return updated;
                if (created) settings.Add(current);
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
            return saved.IsFailure ? saved.Error : await reader.FindAsync(cancellationToken);
        });
    }
}
