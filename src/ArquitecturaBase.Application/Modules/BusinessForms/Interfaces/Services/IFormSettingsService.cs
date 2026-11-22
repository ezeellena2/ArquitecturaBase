using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>Configuración › Formularios (RC4-A): read it and save it with the revision read.</summary>
public interface IFormSettingsService
{
    Task<Result<FormSettingsResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result<FormSettingsResponse>> UpdateAsync(UpdateFormSettingsRequest request, CancellationToken cancellationToken);
}
