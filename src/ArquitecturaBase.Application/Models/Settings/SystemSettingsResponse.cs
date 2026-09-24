using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>Los ajustes guardados que ve el panel de administración.</summary>
public sealed record SystemSettingsResponse(RegistrationMode RegistrationMode);
