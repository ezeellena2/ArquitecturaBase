using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

public sealed record UpdateSystemSettingsSectionRequest(
    long ExpectedRevision,
    string? DefaultCulture = null,
    string? DefaultTimeZoneId = null,
    int? DefaultPageSize = null,
    RegistrationMode? RegistrationMode = null);
