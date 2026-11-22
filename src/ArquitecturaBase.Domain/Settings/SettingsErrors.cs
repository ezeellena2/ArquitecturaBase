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

    public static readonly Error AppearanceInvalid = Error.Validation(AppearanceInvalidCode, "The appearance colors must use the #RRGGBB format.");
    public static readonly Error BrandingInvalid = Error.Validation(BrandingInvalidCode,
        "The application name and the sender name need one line of text, and the email logo an https address.");
    public static readonly Error RetentionInvalid = Error.Validation(RetentionInvalidCode, "A retention period is out of its range.");

    public static readonly Error RevisionConflict = Error.Conflict(RevisionConflictCode, "The system settings have changed. Review the current values.");
    public static readonly Error CultureInvalid = Error.Validation(CultureInvalidCode, "The default culture is not supported.");
    public static readonly Error PageSizeInvalid = Error.Validation(PageSizeInvalidCode, "The default page size is not supported.");

    /// <summary>No existe la fila de ajustes. La crea el seed: si falta, la base quedó a medio preparar.</summary>
    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The system settings were not found.");
}
