using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.GetSystemSettings;

/// <summary>Los ajustes que ve el panel. Por ahora hay uno solo; cada ajuste nuevo suma una propiedad acá.</summary>
public sealed record SystemSettingsResponse(RegistrationMode RegistrationMode);
