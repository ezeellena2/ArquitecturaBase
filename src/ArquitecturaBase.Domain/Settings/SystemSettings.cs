using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// Ajustes del sistema. Es una tabla de una sola fila y la garantía es la clave primaria: siempre vale
/// <see cref="SingletonId"/>, así que un segundo INSERT choca con la PK. Es auditable porque un ajuste que decide
/// quién entra tiene que dejar rastro de quién lo cambió y cuándo (sección 5 del spec de la Fase 4).
/// Cada ajuste nuevo es una propiedad con su tipo y su migración, no un par clave-valor sin forma.
/// </summary>
public sealed class SystemSettings : Entity, IAuditable
{
    /// <summary>La clave de la única fila, como texto, para poder escribirla en el CHECK de la base.</summary>
    public const string SingletonIdValue = "00000000-0000-0000-0000-000000000001";

    /// <summary>La clave de la única fila.</summary>
    public static readonly Guid SingletonId = new(SingletonIdValue);

    public const string InitialCulture = "es";
    public const string InitialTimeZoneId = "America/Argentina/Buenos_Aires";
    public const int InitialPageSize = 20;

    public static bool IsSupportedCulture(string? culture) => culture is "es" or "en";

    public static bool IsSupportedPageSize(int size) => size is 10 or 20 or 50 or 100;

    // Para EF Core y para Create.
    private SystemSettings()
        : base(SingletonId)
    {
    }

    public RegistrationMode RegistrationMode { get; private set; }

    public string DefaultCulture { get; private set; } = InitialCulture;

    public string DefaultTimeZoneId { get; private set; } = InitialTimeZoneId;

    public int DefaultPageSize { get; private set; } = InitialPageSize;

    public long Revision { get; private set; } = 1;

    public string PrimaryColor { get; private set; } = AppearancePalette.InitialPrimaryColor;
    public string MenuColor { get; private set; } = AppearancePalette.InitialMenuColor;
    public string HeaderColor { get; private set; } = AppearancePalette.InitialHeaderColor;
    public string AlternateRowColor { get; private set; } = AppearancePalette.InitialAlternateRowColor;

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public static SystemSettings Create(RegistrationMode registrationMode) =>
        new() { RegistrationMode = registrationMode };

    public void SetRegistrationMode(RegistrationMode registrationMode)
    {
        RegistrationMode = registrationMode;
        Revision++;
    }

    public Result SetDefaultCulture(string culture)
    {
        if (!IsSupportedCulture(culture))
        {
            return SettingsErrors.CultureInvalid;
        }

        DefaultCulture = culture;
        Revision++;
        return Result.Success();
    }

    public Result SetDefaultPageSize(int size)
    {
        if (!IsSupportedPageSize(size))
        {
            return SettingsErrors.PageSizeInvalid;
        }

        DefaultPageSize = size;
        Revision++;
        return Result.Success();
    }

    /// <summary>The application validates the IANA identifier before changing it.</summary>
    public void SetDefaultTimeZoneId(string timeZoneId)
    {
        DefaultTimeZoneId = timeZoneId;
        Revision++;
    }

    /// <summary>Validates the pure rules before applying a group of settings with one revision.
    /// The application validates the IANA identifier and registration enum before calling this method.</summary>
    public Result Update(string? culture, string? timeZoneId, int? pageSize, RegistrationMode? registrationMode,
        AppearancePalette? appearance = null)
    {
        if (culture is not null && !IsSupportedCulture(culture))
        {
            return SettingsErrors.CultureInvalid;
        }

        if (pageSize is { } size && !IsSupportedPageSize(size))
        {
            return SettingsErrors.PageSizeInvalid;
        }

        if (appearance is { IsValid: false })
        {
            return SettingsErrors.AppearanceInvalid;
        }

        if (appearance is not null)
        {
            PrimaryColor = appearance.PrimaryColor.ToUpperInvariant();
            MenuColor = appearance.MenuColor.ToUpperInvariant();
            HeaderColor = appearance.HeaderColor.ToUpperInvariant();
            AlternateRowColor = appearance.AlternateRowColor.ToUpperInvariant();
        }

        if (culture is not null) DefaultCulture = culture;
        if (timeZoneId is not null) DefaultTimeZoneId = timeZoneId;
        if (pageSize is { } validSize) DefaultPageSize = validSize;
        if (registrationMode is { } mode) RegistrationMode = mode;
        Revision++;
        return Result.Success();
    }
}
