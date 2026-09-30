using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface ISystemSettingsService
{
    Task<Result<SystemSettingsResponse>> GetAsync(CancellationToken cancellationToken);

    Task<Result> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken);
    Task<Result> UpdateSectionAsync(UpdateSystemSettingsSectionRequest request, CancellationToken cancellationToken);
    Task<Result<SystemPresentationResponse>> GetPresentationAsync(CancellationToken cancellationToken);
}
