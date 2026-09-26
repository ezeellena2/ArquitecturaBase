using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Api.Contracts.Settings;

/// <summary>El cuerpo de PUT /api/settings.</summary>
public sealed record UpdateSystemSettingsHttpRequest(RegistrationMode RegistrationMode);
