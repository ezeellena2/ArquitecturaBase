using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>Public presentation defaults, without administrative or registration information.</summary>
public sealed record SystemPresentationResponse(string DefaultCulture, string DefaultTimeZoneId, int DefaultPageSize)
{
    public static SystemPresentationResponse Defaults { get; } = new(
        SystemSettings.InitialCulture, SystemSettings.InitialTimeZoneId, SystemSettings.InitialPageSize);
}
