using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>Configuración › WhatsApp (RC3-A): read the settings and save them whole with the revision read.</summary>
public interface IWhatsAppSettingsService
{
    Task<Result<WhatsAppSettingsResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Saves the three values; 409 (<c>WhatsApp.Settings.RevisionConflict</c>) if another save came first.</summary>
    Task<Result<WhatsAppSettingsResponse>> UpdateAsync(UpdateWhatsAppSettingsRequest request, CancellationToken cancellationToken);
}
