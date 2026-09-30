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
}
