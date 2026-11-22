using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Api.Contracts.Settings;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSystemSettingsSectionHttpRequest
{
    private string? _culture;
    private string? _zone;
    private int? _size;
    private RegistrationMode? _mode;
    private SystemAppearanceHttpRequest? _appearance;
    private SystemBrandingHttpRequest? _branding;
    private IdentityRetentionHttpRequest? _retention;

    public SystemAppearanceHttpRequest? Appearance
    {
        get => _appearance;
        init => _appearance = value ?? throw new JsonException("A setting cannot be null.");
    }

    public SystemBrandingHttpRequest? Branding
    {
        get => _branding;
        init => _branding = value ?? throw new JsonException("A setting cannot be null.");
    }

    public IdentityRetentionHttpRequest? Retention
    {
        get => _retention;
        init => _retention = value ?? throw new JsonException("A setting cannot be null.");
    }

    public long ExpectedRevision { get; init; }

    public string? DefaultCulture
    {
        get => _culture;
        init => _culture = value ?? throw new JsonException("A setting cannot be null.");
    }

    public string? DefaultTimeZoneId
    {
        get => _zone;
        init => _zone = value ?? throw new JsonException("A setting cannot be null.");
    }

    public int? DefaultPageSize
    {
        get => _size;
        init => _size = value ?? throw new JsonException("A setting cannot be null.");
    }

    public RegistrationMode? RegistrationMode
    {
        get => _mode;
        init => _mode = value ?? throw new JsonException("A setting cannot be null.");
    }
}
