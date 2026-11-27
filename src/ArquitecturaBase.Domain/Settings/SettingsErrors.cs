using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Settings;

public static class SettingsErrors
{
    public const string NotFoundCode = "Settings.System.NotFound";
    public const string RevisionConflictCode = "Settings.System.RevisionConflict";
    public const string CultureInvalidCode = "Settings.System.CultureInvalid";
    public const string PageSizeInvalidCode = "Settings.System.PageSizeInvalid";
    public const string AppearanceInvalidCode = "Settings.System.AppearanceInvalid";
    public const string BrandingInvalidCode = "Settings.System.BrandingInvalid";
    public const string RetentionInvalidCode = "Settings.System.RetentionInvalid";
    public const string LogoInvalidCode = "Settings.System.LogoInvalid";
    public const string IconInvalidCode = "Settings.System.IconInvalid";

    public static readonly Error AppearanceInvalid = Error.Validation(AppearanceInvalidCode,
        "The appearance colors must use the #RRGGBB format, the menu accent must read on the menu and the font must be supported.");
    public static readonly Error BrandingInvalid = Error.Validation(BrandingInvalidCode,
        "The application name and the sender name need one line of text.");
    public static readonly Error LogoInvalid = Error.Validation(LogoInvalidCode,
        "The logo must be a PNG or JPEG image up to 1 MB, at least as wide as it is tall.");
    public static readonly Error IconInvalid = Error.Validation(IconInvalidCode,
        "The icon must be a square PNG image from 64 to 1024 pixels, up to 1 MB.");
    public static readonly Error RetentionInvalid = Error.Validation(RetentionInvalidCode, "A retention period is out of its range.");

    public static readonly Error RevisionConflict = Error.Conflict(RevisionConflictCode, "The system settings have changed. Review the current values.");
    public static readonly Error CultureInvalid = Error.Validation(CultureInvalidCode, "The default culture is not supported.");
    public static readonly Error PageSizeInvalid = Error.Validation(PageSizeInvalidCode, "The default page size is not supported.");

    /// <summary>No existe la fila de ajustes. La crea el seed: si falta, la base quedó a medio preparar.</summary>
    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The system settings were not found.");
}
